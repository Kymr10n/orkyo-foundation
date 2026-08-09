// Minimal read-only ZIP reader, enough for .xlsx.
//
// An .xlsx is a ZIP of XML parts. Reading a few of those parts does not justify
// a dependency: the one we had (exceljs) pulled archiver -> glob@7 ->
// brace-expansion, whose advisories blocked a release — and a library's
// `overrides` do not protect its consumers.
//
// Deflate is handed to the platform's DecompressionStream('deflate-raw'), which
// exists across the supported browser floor (Safari 16.4+, Chrome 111+, FF 128+)
// and in Node 18+ for tests. Nothing here implements compression itself.

const EOCD_SIGNATURE = 0x06054b50;
const CENTRAL_SIGNATURE = 0x02014b50;
const LOCAL_SIGNATURE = 0x04034b50;
const ZIP64_EOCD_LOCATOR = 0x07064b50;
/** Central-directory fixed header, before the variable name/extra/comment fields. */
const CENTRAL_HEADER_BYTES = 46;
const LOCAL_HEADER_BYTES = 30;
const EOCD_MIN_BYTES = 22;
/** ZIP's "this field overflowed, look in the zip64 record" sentinel. */
const ZIP64_SENTINEL = 0xffffffff;

export class ZipFormatError extends Error {}

interface CentralEntry {
  name: string;
  compressionMethod: number;
  compressedSize: number;
  localHeaderOffset: number;
}

function findEndOfCentralDirectory(view: DataView): number {
  // The EOCD sits at the end, after a comment of up to 64 KB. Scan backwards.
  const maxScan = Math.min(view.byteLength, EOCD_MIN_BYTES + 0xffff);
  for (let i = EOCD_MIN_BYTES; i <= maxScan; i++) {
    const offset = view.byteLength - i;
    if (view.getUint32(offset, true) === EOCD_SIGNATURE) return offset;
  }
  throw new ZipFormatError('Not a ZIP file (no end-of-central-directory record) — is this really an .xlsx?');
}

function readCentralDirectory(buffer: ArrayBuffer): Map<string, CentralEntry> {
  const view = new DataView(buffer);
  const eocd = findEndOfCentralDirectory(view);

  if (eocd >= 20 && view.getUint32(eocd - 20, true) === ZIP64_EOCD_LOCATOR) {
    throw new ZipFormatError('ZIP64 archives are not supported — this file is far larger than a planning workbook.');
  }

  const entryCount = view.getUint16(eocd + 10, true);
  let offset = view.getUint32(eocd + 16, true);
  if (offset === ZIP64_SENTINEL) {
    throw new ZipFormatError('ZIP64 archives are not supported — this file is far larger than a planning workbook.');
  }

  const decoder = new TextDecoder();
  const entries = new Map<string, CentralEntry>();
  for (let i = 0; i < entryCount; i++) {
    if (view.getUint32(offset, true) !== CENTRAL_SIGNATURE) {
      throw new ZipFormatError('Corrupt ZIP central directory.');
    }
    const nameLength = view.getUint16(offset + 28, true);
    const extraLength = view.getUint16(offset + 30, true);
    const commentLength = view.getUint16(offset + 32, true);
    const name = decoder.decode(new Uint8Array(buffer, offset + CENTRAL_HEADER_BYTES, nameLength));
    entries.set(name, {
      name,
      compressionMethod: view.getUint16(offset + 10, true),
      compressedSize: view.getUint32(offset + 20, true),
      localHeaderOffset: view.getUint32(offset + 42, true),
    });
    offset += CENTRAL_HEADER_BYTES + nameLength + extraLength + commentLength;
  }
  return entries;
}

async function inflateRaw(bytes: Uint8Array): Promise<Uint8Array> {
  const stream = new Blob([bytes as BlobPart]).stream().pipeThrough(new DecompressionStream('deflate-raw'));
  return new Uint8Array(await new Response(stream).arrayBuffer());
}

/**
 * Reads a ZIP into a name → text map. Entries are decoded as UTF-8, which is
 * what every part of an .xlsx is.
 */
export async function readZipEntries(buffer: ArrayBuffer): Promise<Map<string, string>> {
  const entries = readCentralDirectory(buffer);
  const view = new DataView(buffer);
  const decoder = new TextDecoder();
  const out = new Map<string, string>();

  for (const entry of entries.values()) {
    if (entry.name.endsWith('/')) continue; // directory marker
    const local = entry.localHeaderOffset;
    if (view.getUint32(local, true) !== LOCAL_SIGNATURE) {
      throw new ZipFormatError(`Corrupt ZIP entry "${entry.name}".`);
    }
    // The local header repeats the name and carries its own extra field, whose
    // length routinely differs from the central directory's — the data offset
    // must be computed from the LOCAL header, not the central one.
    const nameLength = view.getUint16(local + 26, true);
    const extraLength = view.getUint16(local + 28, true);
    const dataStart = local + LOCAL_HEADER_BYTES + nameLength + extraLength;
    const raw = new Uint8Array(buffer, dataStart, entry.compressedSize);

    let bytes: Uint8Array;
    if (entry.compressionMethod === 0) {
      bytes = raw;
    } else if (entry.compressionMethod === 8) {
      bytes = await inflateRaw(raw);
    } else {
      throw new ZipFormatError(`Unsupported ZIP compression method ${entry.compressionMethod} in "${entry.name}".`);
    }
    out.set(entry.name, decoder.decode(bytes));
  }
  return out;
}
