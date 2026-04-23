using Npgsql;

namespace Api.Services;

public static class SiteFloorplanCommandFactory
{
    public static NpgsqlCommand CreateUpdateFloorplanCommand(
        NpgsqlConnection connection,
        Guid siteId,
        string imagePath,
        string mimeType,
        long fileSizeBytes,
        int widthPx,
        int heightPx)
    {
        var command = new NpgsqlCommand(SiteFloorplanQueryContract.BuildUpdateFloorplanSql(), connection);
        command.Parameters.AddWithValue(SiteFloorplanQueryContract.ImagePathParameterName, imagePath);
        command.Parameters.AddWithValue(SiteFloorplanQueryContract.MimeTypeParameterName, mimeType);
        command.Parameters.AddWithValue(SiteFloorplanQueryContract.FileSizeBytesParameterName, fileSizeBytes);
        command.Parameters.AddWithValue(SiteFloorplanQueryContract.WidthPxParameterName, widthPx);
        command.Parameters.AddWithValue(SiteFloorplanQueryContract.HeightPxParameterName, heightPx);
        command.Parameters.AddWithValue(SiteFloorplanQueryContract.SiteIdParameterName, siteId);
        return command;
    }
}
