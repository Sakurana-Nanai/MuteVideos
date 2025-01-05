using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.IO.Compression;
using System.Threading.Tasks;

class MuteVideos
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("ドラッグ＆ドロップした動画ファイルの音声を消去します。");

        // FFmpegの準備
        string ffmpegPath = await EnsureFFmpeg();

        if (args.Length == 0)
        {
            Console.WriteLine("動画ファイルをドラッグ＆ドロップしてください。");
            return;
        }

        foreach (string filePath in args)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    Console.WriteLine($"ファイルが見つかりません: {filePath}");
                    continue;
                }

                string directory = Path.GetDirectoryName(filePath);
                string fileNameWithoutExt = Path.GetFileNameWithoutExtension(filePath);
                string extension = Path.GetExtension(filePath);

                // "_muted"を付けたファイル名
                string outputPath = Path.Combine(directory, $"{fileNameWithoutExt}_muted{extension}");

                Console.WriteLine($"処理中: {filePath}");
                RemoveAudio(filePath, outputPath, ffmpegPath);

                Console.WriteLine($"音声を削除した動画を出力しました: {outputPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"エラーが発生しました: {ex.Message}");
            }
        }

        Console.WriteLine("処理が完了しました。キーを押して終了してください。");
        Console.ReadKey();
    }

    static async Task<string> EnsureFFmpeg()
    {
        string ffmpegDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg");
        string ffmpegExePath = Path.Combine(ffmpegDir, "ffmpeg.exe");
        string ffmpegLinuxPath = "ffmpeg"; // Linuxではシステムパス内を想定

        // 実行中のOSを判別
        if (Environment.OSVersion.Platform == PlatformID.Win32NT)
        {
            // Windowsの場合
            if (File.Exists(ffmpegExePath))
            {
                return ffmpegExePath;
            }

            Console.WriteLine("FFmpegが見つかりません。ダウンロードを開始します...");

            try
            {
                string ffmpegZipUrl = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip";
                string ffmpegZipPath = Path.Combine(ffmpegDir, "ffmpeg.zip");

                // フォルダ作成
                if (!Directory.Exists(ffmpegDir))
                {
                    Directory.CreateDirectory(ffmpegDir);
                }

                // FFmpeg ZIPのダウンロード
                using (HttpClient httpClient = new HttpClient())
                {
                    var response = await httpClient.GetAsync(ffmpegZipUrl, HttpCompletionOption.ResponseHeadersRead);
                    response.EnsureSuccessStatusCode();

                    using (var contentStream = await response.Content.ReadAsStreamAsync())
                    {
                        await DownloadFileWithProgress(contentStream, ffmpegZipPath, response.Content.Headers.ContentLength);
                    }
                }

                // ZIPファイルを展開
                await ExtractZipWithProgress(ffmpegZipPath, ffmpegDir);

                // 展開されたフォルダからffmpeg.exeを見つける
                string extractedDir = Directory.GetDirectories(ffmpegDir)[0];
                string extractedFFmpegPath = Path.Combine(extractedDir, "bin", "ffmpeg.exe");

                // 必要なファイルをffmpegDir直下に移動
                File.Move(extractedFFmpegPath, ffmpegExePath);

                // ZIPファイルと展開されたフォルダを削除
                File.Delete(ffmpegZipPath);
                Directory.Delete(extractedDir, true);

                Console.WriteLine("FFmpegのダウンロードとセットアップが完了しました。");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FFmpegのセットアップ中にエラーが発生しました: {ex.Message}");
                throw;
            }

            return ffmpegExePath;
        }
        else if (Environment.OSVersion.Platform == PlatformID.Unix || Environment.OSVersion.Platform == PlatformID.MacOSX)
        {
            // LinuxまたはMacの場合
            try
            {
                // FFmpegがインストールされているか確認
                ProcessStartInfo processStartInfo = new ProcessStartInfo
                {
                    FileName = ffmpegLinuxPath,
                    Arguments = "-version",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                using (Process process = new Process())
                {
                    process.StartInfo = processStartInfo;
                    process.Start();
                    process.WaitForExit();

                    if (process.ExitCode != 0)
                    {
                        throw new Exception("FFmpegが正しくインストールされていません。");
                    }
                }
            }
            catch
            {
                throw new Exception("FFmpegがインストールされていないか、パスが設定されていません。");
            }

            return ffmpegLinuxPath; // Linux/Macでは単に"ffmpeg"を返す
        }
        else
        {
            throw new PlatformNotSupportedException("現在のOSはサポートされていません。");
        }
    }

    static async Task DownloadFileWithProgress(Stream contentStream, string destinationPath, long? totalSize)
    {
        const int bufferSize = 8192;
        byte[] buffer = new byte[bufferSize];
        long totalRead = 0;
        int read;
        double progress = 0;
        double lastProgress = 0;

        using (FileStream fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, true))
        {
            while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, read);
                totalRead += read;

                if (totalSize.HasValue)
                {
                    progress = (totalRead / (double)totalSize.Value) * 100;
                    if (progress - lastProgress >= 10)
                    {
                        Console.WriteLine($"ダウンロード進行中: {totalRead}/{totalSize} bytes ({progress:F0}%)");
                        lastProgress = progress;
                    }
                }
                else
                {
                    Console.WriteLine($"ダウンロード進行中: {totalRead} bytes");
                }
            }
        }
    }

    static async Task ExtractZipWithProgress(string zipPath, string extractPath)
    {
        using (ZipArchive archive = ZipFile.OpenRead(zipPath))
        {
            long totalSize = archive.Entries.Sum(e => e.Length);
            long totalExtracted = 0;
            double progress = 0;
            double lastProgress = 0;

            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                string destinationPath = Path.Combine(extractPath, entry.FullName);

                if (entry.Name == "")
                {
                    // これはディレクトリの場合
                    Directory.CreateDirectory(destinationPath);
                    continue;
                }

                // ファイルを展開
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
                long fileSize = entry.Length;

                using (Stream source = entry.Open())
                using (FileStream destination = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await source.CopyToAsync(destination);
                }

                totalExtracted += fileSize;
                progress = (totalExtracted / (double)totalSize) * 100;
                if (progress - lastProgress >= 10)
                {
                    Console.WriteLine($"展開進行中: {totalExtracted}/{totalSize} bytes ({progress:F0}%)");
                    lastProgress = progress;
                }
            }
        }
    }

    static void RemoveAudio(string inputPath, string outputPath, string ffmpegPath)
    {
        string arguments = $"-i \"{inputPath}\" -c copy -an \"{outputPath}\"";

        ProcessStartInfo processStartInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using (Process process = new Process())
        {
            process.StartInfo = processStartInfo;
            process.Start();

            // エラーと標準出力を読み取り（デバッグ用）
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                throw new Exception($"FFmpegエラー: {error}");
            }
        }
    }
}
