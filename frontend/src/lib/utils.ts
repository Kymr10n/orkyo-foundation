/**
 * Flat re-export for ESM compatibility.
 * Publishing tsc output with `moduleResolution: "Bundler"` produces imports
 * like `../../lib/utils` that Node.js ESM rejects when `lib/utils/` is a
 * directory (directory imports are unsupported in ESM). This file ensures a
 * `lib/utils.js` sibling exists in the build output so Node.js resolves the
 * import to a file.
 */
export * from "./utils/index";
