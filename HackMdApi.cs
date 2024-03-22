using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Remote;
using OpenQA.Selenium.Support.UI;
using OtpNet;

namespace HackMdBackup;

public class HackMdApi
{
    public string GitHubUsername { get; set; }
    public string GitHubPassword { get; set; }
    public string GitHub2FaSeed { get; set; }
    public string HackMdHost { get; set; }

    private string _sessionCookie;
    private string _crsfCookie;
    private string _useridCookie;
    private readonly HttpClient _client = new();
    private readonly string _seleniumHost;
    private readonly bool _forceAuth;

    private string CredsPathFull => Path.Combine(CredetialCachePath, "cookie_cache.json");

    public HackMdApi(string seleniumHost, bool forceAuth = false)
    {
        _seleniumHost = seleniumHost;
        _forceAuth = forceAuth;
        
    }

    private string GetTotp()
    {
        // Replace this with your secret key
        byte[] secretKey = Base32Encoding.ToBytes(GitHub2FaSeed);

        var totp = new Totp(secretKey);
        return totp.ComputeTotp();
    }

    private void GrabSessionCookie()
    {
        // Instantiate a ChromeDriver
        var options = new ChromeOptions();
        options.AddArgument("--headless=new");
        
        using IWebDriver driver = new RemoteWebDriver(new Uri(_seleniumHost),options);
        WebDriverWait wait = new WebDriverWait(driver, TimeSpan.FromSeconds(120));

        Console.WriteLine("\tNavigating to login page...");
        // Navigate to Notes GitHub auth
        driver.Navigate().GoToUrl($"https://{HackMdHost}/auth/github");

        // Login to GitHub
        Console.WriteLine("\tEntering credentials...");
        IWebElement loginInput = driver.FindElement(By.Id("login_field"));
        loginInput.SendKeys(GitHubUsername);

        IWebElement passInput = driver.FindElement(By.Id("password"));
        passInput.SendKeys(GitHubPassword);

        Console.WriteLine("\tSending login...");
        IWebElement loginBtn = driver.FindElement(By.Name("commit"));
        loginBtn.Click();

        Console.WriteLine("\tWaiting for 2FA page...");
        // Wait for passkey prompt
        wait.Until(webDriver => webDriver.Url.Contains("two-factor"));

        Console.WriteLine("\tFilling 2FA token...");
        // Grab current TOTP
        string token = GetTotp();

        IWebElement mfaInput = driver.FindElement(By.Id("app_totp"));
        mfaInput.SendKeys(token);

        // Wait for return to HackMD
        Console.WriteLine("\tWaiting for return to HackMD...");
        wait.Until(webDriver => webDriver.Url.Contains(HackMdHost));

        Console.WriteLine("\tGrabbing delicious Cookies...");
        // grab cookies
        var sessionCookie = driver.Manage().Cookies.GetCookieNamed("connect.sid");
        if (sessionCookie == null)
        {
            throw new Exception("No session cookie");
        }

        _sessionCookie = sessionCookie.Value;
        _crsfCookie = driver.Manage().Cookies.GetCookieNamed("_csrf").Value;
        _useridCookie = driver.Manage().Cookies.GetCookieNamed("userid").Value;
        
        // Close the driver
        driver.Quit();
    }

    private bool LoadCachedCookies()
    {
        if (!File.Exists(CredsPathFull))
            return false;

        string json = File.ReadAllText(CredsPathFull);
        CachedCookies? cookies = JsonSerializer.Deserialize<CachedCookies>(json);

        if (cookies == null)
            return false;

        // Verify cached cookies with current credentials
        string currentCredentials = ComputeSha256Hash($"{GitHubUsername}:{GitHubPassword}:{GitHub2FaSeed}");
        if (currentCredentials != cookies.CredentialHash)
            return false;
        
        _crsfCookie = cookies.CRSF;
        _sessionCookie = cookies.Session;
        _useridCookie = cookies.UserId;
        
        return true;
    }

    private void StoreCachedCookies()
    {
        CachedCookies cookies = new CachedCookies
        {
            Session = _sessionCookie,
            UserId = _useridCookie,
            CRSF = _crsfCookie,
            CredentialHash = ComputeSha256Hash($"{GitHubUsername}:{GitHubPassword}:{GitHub2FaSeed}")
        };

        string jsonString = JsonSerializer.Serialize(cookies);
        
        File.WriteAllText(CredsPathFull, jsonString);
    }
   
    private string ComputeSha256Hash(string rawData)
    {
        // Create a SHA256   
        using SHA256 sha256Hash = SHA256.Create();
        // ComputeHash - returns byte array  
        byte[] bytes = sha256Hash.ComputeHash(Encoding.UTF8.GetBytes(rawData));

        // Convert byte array to a string   
        StringBuilder builder = new StringBuilder();
        foreach (var b in bytes)
        {
            builder.Append(b.ToString("x2"));
        }
        return builder.ToString();
    }

    
    public async Task GetAllNotes()
    {
        if (_forceAuth || !LoadCachedCookies())
        {
            Console.WriteLine("No cached credentials. Grabbing fresh Cookies...");
            GrabSessionCookie();

            StoreCachedCookies();
            Console.WriteLine("Cookies cache created.");
        }
        else
        {
            Console.WriteLine("Using cached cookies.");
        }
      
        // Grab notes. as long as we get new notes we increase the counter
        bool returnedNotes = true;
        int page = 1;
        List<NoteMeta> allNotes = new List<NoteMeta>();
        
        while (returnedNotes)
        {
            var request = CreateRequest($"/api/notes?page={page}");
            var response = await _client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            
            string responseBody = await response.Content.ReadAsStringAsync();
            NotesResponse notes = JsonSerializer.Deserialize <NotesResponse>(responseBody)!;

            if (notes.result.Count == 0)
            {
                // No more notes
                returnedNotes = false;
                Console.WriteLine("Got all note metadata.");
            }
            else
            {
                allNotes.AddRange(notes.result);
                Console.WriteLine($"Page {page}: Got {notes.result.Count} - Total {allNotes.Count}");
                page++;
            }
        }

        Console.WriteLine("Start reading notes...");
        int ct = 1;
        int total = allNotes.Count;
        List<NoteMeta> badNotes = new List<NoteMeta>();
        foreach (var note in allNotes)
        {
            Console.WriteLine($"\tGrabbing({ct}/{total}) [{note.shortId}] {note.title}...");
            try
            {
                string category = "uncategorized";
                if(note.team != null)
                    category = $"team/{note.team.path}";
                else if (note.owner != null)
                    category = $"user/{note.owner.userpath}";
                string catPath = Path.Combine(BackupPath, category);

                // Ensure dir exists
                Directory.CreateDirectory(catPath);

                string metadataPath = Path.Combine(catPath, $"{note.shortId}.json");

                if (File.Exists(metadataPath))
                {
                    Console.WriteLine("\t\tExisting. Skip...");
                    ct++;
                    continue;
                }
                
                // Write metadata file
                await File.WriteAllTextAsync(metadataPath, JsonSerializer.Serialize(note));

                // Pull note
                var req = CreateRequest($"/api/note/{note.id}");
                var response = await _client.SendAsync(req);
                response.EnsureSuccessStatusCode();

                string responseBody = await response.Content.ReadAsStringAsync();
                NoteResponse noteData = JsonSerializer.Deserialize<NoteResponse>(responseBody)!;

                // Write content
                await File.WriteAllTextAsync(Path.Combine(catPath, $"{note.shortId}.md"), noteData.result.content);
            }
            catch (Exception)
            {
                badNotes.Add(note);
                Console.WriteLine("\t\tError grabbing.");
            }
            ct++;
        }
        
        await File.WriteAllTextAsync(Path.Combine(BackupPath,"error_notes.json"),JsonSerializer.Serialize(badNotes)); 
        
    }

    public string BackupPath { get; set; }
    public string CredetialCachePath { get; set; }

    private HttpRequestMessage CreateRequest(string path)
    {
       return new HttpRequestMessage
        {
            Method = HttpMethod.Get,
            RequestUri = new Uri($"https://{HackMdHost}{path}"),
            Headers =
            {
                { "accept", "application/json, text/plain, */*" },
                {
                    "cookie",
                    $"locale=en-GB; connect.sid={_sessionCookie}; _csrf={_crsfCookie}; loginstate=true; userid={_useridCookie}"
                },
                { "referer", $"https://{HackMdHost}/dashboard/note" },
                {
                    "user-agent",
                    "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36"
                },
                { "x-xsrf-token", "FTxa1K8n-Bb877mY1mqBvZJNiWRqUf4sIqh0" },
            },
        };
    }
}

internal class CachedCookies
{
    public string Session { get; set; }
    public string UserId { get; set; }
    public string CRSF { get; set; }
    public string CredentialHash { get; set; }
}