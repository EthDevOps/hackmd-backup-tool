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
    private readonly bool _testMode;

    private string CredsPathFull => Path.Combine(CredetialCachePath, "cookie_cache.json");

    public HackMdApi(string seleniumHost, bool forceAuth = false, bool testMode = false)
    {
        _seleniumHost = seleniumHost;
        _forceAuth = forceAuth;
        _testMode = testMode;
    }

    private void WaitForInput(string stepDescription)
    {
        if (!_testMode) return;
        Console.WriteLine($"\t[TEST] {stepDescription}");
        Console.WriteLine("\t[TEST] Press ENTER to continue...");
        Console.ReadLine();
    }

    private string GetTotp()
    {
        // Replace this with your secret key
        byte[] secretKey = Base32Encoding.ToBytes(GitHub2FaSeed);

        var totp = new Totp(secretKey);
        return totp.ComputeTotp();
    }

    private IWebDriver CreateDriver()
    {
        var options = new ChromeOptions();
        options.AddArgument("--disable-webauthn");

        if (_testMode)
        {
            // Local visible browser for debugging
            return new ChromeDriver(options);
        }

        // Headless remote browser for production
        options.AddArgument("--headless=new");
        options.AddArgument("--no-sandbox");
        options.AddArgument("--disable-dev-shm-usage");
        return new RemoteWebDriver(new Uri(_seleniumHost), options);
    }

    private void GrabSessionCookie()
    {
        using IWebDriver driver = CreateDriver();
        WebDriverWait wait = new WebDriverWait(driver, TimeSpan.FromSeconds(120));

        try
        {
            Console.WriteLine("\tNavigating to login page...");
            driver.Navigate().GoToUrl($"https://{HackMdHost}/auth/github");

            // Wait for GitHub login form to be ready (React-rendered)
            Console.WriteLine("\tWaiting for GitHub login form...");
            wait.Until(webDriver =>
            {
                try { return webDriver.FindElement(By.Id("login_field")).Displayed; }
                catch (NoSuchElementException) { return false; }
            });

            Console.WriteLine($"\tCurrent URL: {driver.Url}");
            WaitForInput($"GitHub login form loaded at {driver.Url}");

            // Login to GitHub
            Console.WriteLine("\tEntering credentials...");
            IWebElement loginInput = driver.FindElement(By.Id("login_field"));
            loginInput.Clear();
            loginInput.SendKeys(GitHubUsername);

            IWebElement passInput = driver.FindElement(By.Id("password"));
            passInput.Clear();
            passInput.SendKeys(GitHubPassword);

            WaitForInput("Credentials filled, about to click login");

            Console.WriteLine("\tSending login...");
            IWebElement loginBtn = driver.FindElement(By.Name("commit"));
            loginBtn.Click();

            Console.WriteLine("\tWaiting for 2FA page...");
            wait.Until(webDriver => webDriver.Url.Contains("two-factor") || webDriver.Url.Contains("sessions"));
            Console.WriteLine($"\tCurrent URL: {driver.Url}");
            WaitForInput($"2FA page reached at {driver.Url}");

            // GitHub may show a passkey/security key prompt before TOTP.
            // Try to find and click an "authenticator app" or TOTP link if the
            // TOTP input isn't immediately visible.
            Console.WriteLine("\tLooking for TOTP input...");
            IWebElement mfaInput = WaitForTotpInput(driver, wait);

            Console.WriteLine("\tFilling 2FA token...");
            string token = GetTotp();
            mfaInput.Clear();
            mfaInput.SendKeys(token);

            WaitForInput("TOTP filled, about to submit");

            // Some GitHub 2FA pages require clicking a verify button
            try
            {
                var verifyBtn = driver.FindElement(By.CssSelector("button[type='submit']"));
                if (verifyBtn.Displayed)
                    verifyBtn.Click();
            }
            catch (NoSuchElementException)
            {
                // TOTP auto-submits on some flows
            }

            // Wait for return to HackMD
            Console.WriteLine("\tWaiting for return to HackMD...");
            wait.Until(webDriver => webDriver.Url.Contains(HackMdHost));
            Console.WriteLine($"\tCurrent URL: {driver.Url}");

            // Give HackMD a moment to set cookies after OAuth callback
            Thread.Sleep(3000);
            WaitForInput($"Returned to HackMD at {driver.Url}");

            Console.WriteLine("\tGrabbing delicious Cookies...");
            // Log all cookies in test mode
            if (_testMode)
            {
                Console.WriteLine("\tAll cookies:");
                foreach (var c in driver.Manage().Cookies.AllCookies)
                    Console.WriteLine($"\t\t{c.Name} = {c.Value[..Math.Min(40, c.Value.Length)]}...");
            }

            var sessionCookie = driver.Manage().Cookies.GetCookieNamed("connect.sid");
            if (sessionCookie == null)
            {
                Console.WriteLine("\tAvailable cookies:");
                foreach (var c in driver.Manage().Cookies.AllCookies)
                    Console.WriteLine($"\t\t{c.Name} = {c.Value[..Math.Min(20, c.Value.Length)]}...");
                throw new Exception("No session cookie found after OAuth flow");
            }

            _sessionCookie = sessionCookie.Value;
            _crsfCookie = driver.Manage().Cookies.GetCookieNamed("_csrf")?.Value ?? "";
            _useridCookie = driver.Manage().Cookies.GetCookieNamed("userid")?.Value ?? "";

            Console.WriteLine("\tCookies obtained successfully.");
            WaitForInput("Cookies grabbed, about to close browser");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\tAuth failed at URL: {driver.Url}");
            Console.WriteLine($"\tPage title: {driver.Title}");
            Console.WriteLine($"\tError: {ex.Message}");
            if (_testMode)
            {
                Console.WriteLine("\t[TEST] Browser left open for inspection. Press ENTER to close...");
                Console.ReadLine();
            }
            throw;
        }
        finally
        {
            driver.Quit();
        }
    }

    private IWebElement WaitForTotpInput(IWebDriver driver, WebDriverWait wait)
    {
        // First, try to find the TOTP input directly (old flow) with a short timeout
        var shortWait = new WebDriverWait(driver, TimeSpan.FromSeconds(10));
        try
        {
            var input = shortWait.Until(webDriver =>
            {
                try
                {
                    var el = webDriver.FindElement(By.Id("app_totp"));
                    return el.Displayed ? el : null;
                }
                catch (NoSuchElementException) { return null; }
            });
            if (input != null) return input;
        }
        catch (WebDriverTimeoutException)
        {
            // Not found directly, try alternative flows below
        }

        // GitHub's newer 2FA page may show passkey/security key prompt first.
        // Look for a link to switch to TOTP/authenticator app.
        Console.WriteLine("\tTOTP input not immediately visible, looking for authenticator app link...");
        string[] linkTexts = {
            "Use your authenticator app",
            "Authenticator app",
            "Use a verification code",
            "authenticator app",
            "verification code"
        };

        foreach (var text in linkTexts)
        {
            try
            {
                var link = driver.FindElement(By.PartialLinkText(text));
                if (link.Displayed)
                {
                    Console.WriteLine($"\tClicking '{text}' link...");
                    link.Click();
                    Thread.Sleep(1000);
                    break;
                }
            }
            catch (NoSuchElementException) { }
        }

        // Also try clicking by common button/link patterns
        try
        {
            var totpLinks = driver.FindElements(By.CssSelector("a[href*='totp'], a[href*='app'], button[data-target*='totp']"));
            foreach (var link in totpLinks)
            {
                if (link.Displayed)
                {
                    Console.WriteLine($"\tClicking TOTP link: {link.Text}...");
                    link.Click();
                    Thread.Sleep(1000);
                    break;
                }
            }
        }
        catch (NoSuchElementException) { }

        // Now wait for the TOTP input with multiple possible selectors
        return wait.Until(webDriver =>
        {
            // Try known IDs for the TOTP input
            string[] selectors = { "#app_totp", "#totp", "input[name='app_totp']", "input[name='otp']", "input[autocomplete='one-time-code']" };
            foreach (var selector in selectors)
            {
                try
                {
                    var el = webDriver.FindElement(By.CssSelector(selector));
                    if (el.Displayed) return el;
                }
                catch (NoSuchElementException) { }
            }
            return null;
        });
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
            Console.WriteLine("Testing cached cookies...");
            var request = CreateRequest($"/api/notes?page=1");
            var response = await _client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine("Cached credentials expired. Re-authenticating...");
                GrabSessionCookie();
                StoreCachedCookies();
                Console.WriteLine("Cookies cache created.");
            }
            else
            {
                Console.WriteLine("Using cached cookies.");
            }
        }

        // Grab notes. as long as we get new notes we increase the counter
        bool returnedNotes = true;
        int page = 1;
        List<NoteMeta> allNotes = new List<NoteMeta>();
        Random random = new Random();

        while (returnedNotes)
        {
            var request = CreateRequest($"/api/notes?page={page}");
            var response = await _client.SendAsync(request);
            response.EnsureSuccessStatusCode();

            if (response.Headers.Contains("aws-waf-token"))
            {
                Console.WriteLine("New AWS WAF token received.");
            }

            string responseBody = string.Empty;
            NotesResponse notes = null;
            try
            {
                responseBody = await response.Content.ReadAsStringAsync();
                notes = JsonSerializer.Deserialize<NotesResponse>(responseBody)!;
                Console.WriteLine($"Notes grabbed: {notes.page} of {notes.total} | Limit at {notes.limit}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during list fetch: {ex.Message} == {response.StatusCode}|{response.ReasonPhrase} [{responseBody}]");
            }

            if (notes is { result.Count: 0 })
            {
                // No more notes
                returnedNotes = false;
                Console.WriteLine("Got all note metadata.");
            }
            else if (notes != null)
            {
                allNotes.AddRange(notes.result);
                Console.WriteLine($"Page {page}: Got {notes.result.Count} - Total {allNotes.Count}");
                page++;
            }
            else
            {
                Console.WriteLine("Got caught by the AWS WAF. Waiting for 15 minutes before trying again...");
                Thread.Sleep(TimeSpan.FromMinutes(15));
            }

            Thread.Sleep(random.Next(1000, 10000));
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
                if (note.team != null)
                    category = $"team/{note.team.path}";
                else if (note.owner != null)
                    category = $"user/{note.owner.userpath}";
                string catPath = Path.Combine(BackupPath, category);

                // Ensure dir exists
                Directory.CreateDirectory(catPath);

                // Write metadata file
                string metadataPath = Path.Combine(catPath, $"{note.shortId}.json");
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

        await File.WriteAllTextAsync(Path.Combine(BackupPath, "error_notes.json"), JsonSerializer.Serialize(badNotes));
    }

    public string BackupPath { get; set; }
    public string CredetialCachePath { get; set; }

    private HttpRequestMessage CreateRequest(string path)
    {
       return new HttpRequestMessage
        {
            Method = HttpMethod.Get,
            Version = HttpVersion.Version20,
            RequestUri = new Uri($"https://{HackMdHost}{path}"),
            Headers =
            {
                { "accept", "application/json, text/plain, */*" },
                { "accept-language", "en-GB,en;q=0.5"},
                {
                    "cookie",
                    $"locale=en-GB; indent_type=space; space_units=4; keymap=sublime; connect.sid={_sessionCookie}; _csrf={_crsfCookie}; loginstate=true; userid={_useridCookie}"
                },
                { "referer", $"https://{HackMdHost}/" },
                {
                    "user-agent",
                    "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36"
                },
                { "x-xsrf-token", _crsfCookie ?? "" }
            },
        };
    }

    public void SetCookieExternal(string cookieCsrf, string cookieSessionId, string cookieUserId)
    {
        _crsfCookie = cookieCsrf;
        _sessionCookie = cookieSessionId;
        _useridCookie = cookieUserId;
        StoreCachedCookies();
        Console.WriteLine("Stored externally provided cookie values");
    }
}

internal class CachedCookies
{
    public string Session { get; set; }
    public string UserId { get; set; }
    public string CRSF { get; set; }
    public string CredentialHash { get; set; }
}