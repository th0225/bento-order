using Microsoft.AspNetCore.Components.Forms;

namespace bento_order.Services;

public class MenuUploadService
{
    public MenuUploadService() {}

    public async Task<bool> UploadMenuAsync(Stream fileStream,
        string fileName)
    {
        try
        {
            // 取得副檔名
            var extension = Path.GetExtension(fileName).ToLower();
            // 新檔名
            var newFileName = $"menu{extension}";
            // 儲存資料夾路徑
            var folderPath = Path.Combine(
                Directory.GetCurrentDirectory(), "wwwroot");
            
            // 儲存路徑
            var filePath = Path.Combine(folderPath, newFileName);

            using var fs = new FileStream(filePath, FileMode.Create);
            await fileStream.CopyToAsync(fs);

            return true;
        }
        catch
        {
            return false;
        }
    }
}