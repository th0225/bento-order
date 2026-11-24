namespace bento_order.Services;

// 儲存菜單圖片（已在瀏覽器端壓縮成 JPEG）
public class MenuUploadService
{
    private readonly AppPaths _appPaths;

    public MenuUploadService(AppPaths appPaths)
    {
        _appPaths = appPaths;
    }

    public async Task UploadMenuAsync(Stream fileStream)
    {
        var directory = Path.GetDirectoryName(_appPaths.MenuFilePath)!;
        Directory.CreateDirectory(directory);

        // 先寫到暫存檔再取代，避免寫到一半時有人讀到不完整的圖
        var tempPath = _appPaths.MenuFilePath + ".tmp";
        await using (var fs = new FileStream(tempPath, FileMode.Create))
        {
            await fileStream.CopyToAsync(fs);
        }

        File.Move(tempPath, _appPaths.MenuFilePath, overwrite: true);
    }
}
