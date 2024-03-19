// See https://aka.ms/new-console-template for more information

using HackMdBackup;

string GetConfig(string envName, string defaultValue = "")
{
    string? envVar = Environment.GetEnvironmentVariable(envName);
        
    if(string.IsNullOrEmpty(envVar) && string.IsNullOrEmpty(envVar))
    {
        throw new Exception($"Missing configuration env: {envName}");
    }

    return string.IsNullOrEmpty(envVar) ? defaultValue : envVar;
}

var pingHttp = new HttpClient();

string hcUrl = GetConfig("HEALTHCHECK_URL");
await pingHttp.GetAsync($"{hcUrl}/start");

string gpgPublicKey = GetConfig("GPG_PUBKEY_FILE");
string tarFile = GetConfig("BACKUP_TAR_PATH","/tmp/backup.tar.gz");
string webDriverEndpoint = GetConfig("WEBDRIVER_URL", "http://localhost:4444");

string s3Host = GetConfig("S3_HOST");
string s3AccessKEy = GetConfig("S3_ACCESS_KEY");
string s3SecretKey = GetConfig("S3_SECRET_KEY");
string s3Bucket = GetConfig("S3_BUCKET");

HackMdApi api = new HackMdApi(webDriverEndpoint)
{
    GitHubUsername = GetConfig("GITHUB_USERNAME"),
    GitHubPassword = GetConfig("GITHUB_PASSWORD"),
    GitHub2FaSeed = GetConfig("GITHUB_OTP_SEED"),
    BackupPath = GetConfig("BACKUP_PATH","/tmp/backup"),
    CredetialCachePath = GetConfig("CREDENTIAL_CACHE_PATH","/tmp/creds"),
    HackMdHost = GetConfig("HACKMD_HOST"),
};

Console.WriteLine("==> Pulling notes");
await api.GetAllNotes();

FileProcessor fp = new FileProcessor();

Console.WriteLine("==> Compressing...");
fp.CreateTarGz(tarFile, api.BackupPath);

Console.WriteLine("==> Encrypting...");
string datetime = DateTime.UtcNow.ToString("s").Replace(':', '-');
string encryptedFile = $"{tarFile}.{datetime}.gpg";
fp.EncryptFile(tarFile, gpgPublicKey, encryptedFile, false, true);

Console.WriteLine("==> Send to S3 storage");
var s3 = new S3Uploader(s3Host, s3AccessKEy, s3SecretKey, s3Bucket);
await s3.UploadFileAsync(encryptedFile);

await pingHttp.GetAsync($"{hcUrl}");
Console.WriteLine("done.");

