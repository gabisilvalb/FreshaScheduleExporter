using System.Diagnostics;
using System.Net;
using System.Text;
using Microsoft.Playwright;

class Program
{
    private static readonly string SessionFile = Path.Combine(AppContext.BaseDirectory, "fresha_session.json");
    //private const string WhatsAppApiUrl = "https://graph.facebook.com/v17.0/";
    //private const string WhatsAppPhoneNumberId = "YOUR_WHATSAPP_PHONE_NUMBER_ID"; // Replace with your phone number ID
    //private const string WhatsAppToken = "YOUR_WHATSAPP_ACCESS_TOKEN"; // Replace with your access token

    static async Task Main(string[] args)
    {
        try
        {
            Console.WriteLine("Starting Fresha Schedule Exporter...");

            // Step 1: Export appointments from Fresha (your existing code)
            var exportPath = await ExportAppointmentsFromFreshaAsync();
            Console.WriteLine($"Exported report to: {exportPath}");

            // Step 2: Display table with pre-filled messages for manual copy
            var tomorrow = DateTimeOffset.Now.AddDays(1).ToString("yyyy-MM-dd");
            //var outputHtmlPath = Path.Combine(Directory.GetCurrentDirectory(), $"C:\\Users\\gsgab\\source\\repos\\FreshaScheduleExporter\\HtmlAppontments_{tomorrow}.html");
            string baseDir = AppContext.BaseDirectory;
            string outputHtmlPath = Path.Combine(baseDir, $"HtmlAppointments_{tomorrow}.html");
            ExportMessagesToHtml(exportPath, outputHtmlPath);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }

    private static async Task<string> ExportAppointmentsFromFreshaAsync()
    {
        using var playwright = await Playwright.CreateAsync();
        var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = false });
        var context = await LoadOrCreateContextAsync(browser);

        var page = await context.NewPageAsync();
        await EnsureLoggedInAsync(page);

        var tomorrow = DateTimeOffset.Now.AddDays(1).ToString("yyyy-MM-dd");
        await page.GotoAsync($"https://partners.fresha.com/sales/appointments-list/?report-date-from={tomorrow}&report-date-to={tomorrow}&report-shortcut=tomorrow");
        await page.WaitForTimeoutAsync(2000);

        Console.WriteLine("Exporting report...");
        await page.WaitForSelectorAsync("text='Exportar'");
        await page.ClickAsync("text='Exportar'");
        await page.WaitForSelectorAsync("li:has-text('CSV')");
        await page.ClickAsync("li:has-text('CSV')");

        var csv = await page.WaitForDownloadAsync();
        using var csvStream = await csv.CreateReadStreamAsync();

        string baseDir = AppContext.BaseDirectory;
        var exportPath = Path.Combine(baseDir, $"Appointments_{tomorrow}.csv");
        //var exportPath = Path.Combine(Directory.GetCurrentDirectory(), $"C:\\Users\\gsgab\\source\\repos\\FreshaScheduleExporter\\Appointments_{tomorrow}.csv");

        // Read original CSV content
        var lines = new List<string>();
        using (var reader = new StreamReader(csvStream))
        {
            while (await reader.ReadLineAsync() is string line)
            {
                lines.Add(line);
            }
        }

        using (var writer = new StreamWriter(exportPath))
        {
            bool isHeader = true;
            foreach (var line in lines)
            {
                var columns = line.Split(',').Select(c => c.Trim().Trim('"')).ToArray();
                int referenceIndex = Array.IndexOf(columns, "Referência");

                if (isHeader && referenceIndex >= 0)
                {
                    // Add PhoneNumber to header
                    await writer.WriteLineAsync(line + ",PhoneNumber");
                    isHeader = false;
                }
                else if (!isHeader && referenceIndex < 0)
                {
                    // Process data row
                    var reference = columns.FirstOrDefault();
                    var phoneNumber = !string.IsNullOrEmpty(reference) ? await ExtractPhoneNumberAsync(page, reference) : "";
                    await writer.WriteLineAsync(line + $",\"{phoneNumber}\"");
                    Console.WriteLine($"Added to Reference {reference} the phone number {phoneNumber}");
                }
            }
        }

        await browser.CloseAsync();
        return exportPath;
    }

    private static async Task<string> ExtractPhoneNumberAsync(IPage page, string reference)
    {
        await page.WaitForSelectorAsync($"text='{reference}'", new PageWaitForSelectorOptions { State = WaitForSelectorState.Visible });
        await page.ClickAsync($"text='{reference}'");
        await page.WaitForTimeoutAsync(3000);
        var phoneNumberButton = await page.QuerySelectorAsync("button[data-qa='customer-contact-number']");
        var phoneNumber = phoneNumberButton != null ? await phoneNumberButton.InnerTextAsync() : "Not Found";
        await page.GoBackAsync();
        return phoneNumber;
    }

    private static async Task EnsureLoggedInAsync(IPage page)
    {
        await page.GotoAsync("https://partners.fresha.com/sales/appointments-list/");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        if (page.Url.Contains("/sign-in") || await page.Locator("form[action*='sign-in']").IsVisibleAsync())
        {
            Console.WriteLine("Logging in...");
            await LoginAsync(page);
        }
        else
        {
            Console.WriteLine("Session restored, logged in.");
        }
    }

    private static async Task LoginAsync(IPage page)
    {
        await page.GotoAsync("https://partners.fresha.com/users/sign-in");
        await page.FillAsync("input[name='email']", "lineastudio.pt@gmail.com");
        await page.ClickAsync("button[data-qa='continue']");
        await page.WaitForSelectorAsync("input[name='password']");

        var password = "GatinhaMya2002";
        foreach (char c in password)
        {
            await page.Keyboard.TypeAsync(c.ToString(), new KeyboardTypeOptions { Delay = 300 });
        }

        await page.ClickAsync("button[data-qa='login']");
        await page.WaitForURLAsync(url => !url.Contains("sign-in"));
        Console.WriteLine("Login successful.");
    }

    private static async Task<IBrowserContext> LoadOrCreateContextAsync(IBrowser browser)
    {
        if (File.Exists(SessionFile))
            return await browser.NewContextAsync(new BrowserNewContextOptions { StorageStatePath = SessionFile });

        return await browser.NewContextAsync();
    }

    public static void ExportMessagesToHtml(string csvPath, string outputHtmlPath)
    {
        var lines = File.ReadAllLines(csvPath);
        if (lines.Length < 2) return;

        var header = lines[0].Split(',');

        int nameIndex = Array.FindIndex(header, h => h.Contains("Cliente", StringComparison.OrdinalIgnoreCase));
        int phoneIndex = Array.FindIndex(header, h => h.Contains("PhoneNumber", StringComparison.OrdinalIgnoreCase));
        int timeIndex = Array.FindIndex(header, h => h.Contains("Horário", StringComparison.OrdinalIgnoreCase));
        int dateIndex = Array.FindIndex(header, h => h.Contains("Data agendada", StringComparison.OrdinalIgnoreCase));
        int serviceIndex = Array.FindIndex(header, h => h.Contains("Serviço", StringComparison.OrdinalIgnoreCase));
        int statusIndex = Array.FindIndex(header, h => h.Contains("Situação", StringComparison.OrdinalIgnoreCase));

        var grouped = new Dictionary<string, List<(string Name, string FirstName, string Time, string Date, string Service)>>();

        for (int i = 1; i < lines.Length; i++)
        {
            var cols = lines[i].Split(',').Select(c => c.Trim('"')).ToArray();
            if (cols.Length <= Math.Max(Math.Max(nameIndex, phoneIndex), timeIndex)) continue;

            if (statusIndex >= 0 && cols.Length > statusIndex &&
                cols[statusIndex].Trim().Equals("Cancelado", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            var debug = cols[nameIndex];

            string name = nameIndex >= 0 ? cols[nameIndex] : "Cliente";
            string firstName = nameIndex >= 0 ? cols[nameIndex].Trim(' ').Split(' ')[0] : "Cliente";
            string rawPhone = phoneIndex >= 0 ? cols[phoneIndex] : "Sem número";

            string phone = new string(rawPhone.Where(char.IsDigit).ToArray());
            if (phone.StartsWith("351")) phone = phone[3..];

            string time = timeIndex >= 0 ? cols[timeIndex] : "hora indefinida";
            string date = dateIndex >= 0 ? DateTime.Parse(cols[dateIndex]).ToString("dd 'de' MMMM", new System.Globalization.CultureInfo("pt-PT")) : "data indefinida";
            string service = serviceIndex >= 0 ? cols[serviceIndex] : "serviço";

            if (!grouped.ContainsKey(phone))
                grouped[phone] = new List<(string, string, string, string, string)>();

            grouped[phone].Add((name, firstName, time, date, service));
        }

        var sb = new StringBuilder();

        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"pt\">");
        sb.AppendLine("<head>");
        sb.AppendLine("<meta charset=\"UTF-8\">");
        sb.AppendLine("<title>Mensagens de Agendamento</title>");
        sb.AppendLine("<style>");
        sb.AppendLine("body { font-family: Arial, sans-serif; padding: 20px; }");
        sb.AppendLine("table { border-collapse: collapse; width: 100%; }");
        sb.AppendLine("th, td { border: 1px solid #ccc; padding: 8px; text-align: left; vertical-align: top; }");
        sb.AppendLine("th { background-color: #f2f2f2; }");
        sb.AppendLine("tr:hover { background-color: #f9f9f9; }");
        sb.AppendLine("button { padding: 5px 8px; margin-left: 5px; cursor: pointer; border-radius: 4px; border: 1px solid #aaa; }");
        sb.AppendLine("a button { background-color: #25D366; color: white; font-weight: bold; border: none; }");
        sb.AppendLine("#copy-toast { position: fixed; bottom: 20px; right: 20px; background-color: #4CAF50; color: white; padding: 12px 18px; border-radius: 6px; font-size: 14px; z-index: 9999; display: none; box-shadow: 0 2px 6px rgba(0,0,0,0.2); }");
        sb.AppendLine("</style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine("<h2>Mensagens de Lembrete de Agendamento</h2>");
        sb.AppendLine("<table>");
        sb.AppendLine("<tr><th>Nome</th><th>Número</th><th>Mensagem</th></tr>");

        int msgId = 0;
        foreach (var kvp in grouped)
        {
            var phone = kvp.Key;
            var agendamentos = kvp.Value;
            var earliest = agendamentos.OrderBy(a => DateTime.Parse(a.Time)).First();
            var name = earliest.Name;
            var firstName = earliest.FirstName;
            var date = earliest.Date;
            var time = earliest.Time;
            var services = agendamentos.Select(a => a.Service).Distinct();
            string serviceText = string.Join(", ", services);

            string messageRaw = $"Olá {firstName} 🤍\n" +
                                $"Lembrete: a tua marcação é amanhã, dia {date}, às {time}, para {serviceText} com a Yara.\n\n" +
                                "Se precisares de fazer alguma alteração, é só avisar. 🌸\n\n" +
                                "Com carinho,\n𝐋𝐢𝐧𝐞𝐚 𝐒𝐭𝐮𝐝𝐢𝐨";

            string messageHtml = WebUtility.HtmlEncode(messageRaw).Replace("\n", "<br>");
            string messageEncoded = Uri.EscapeDataString(messageRaw);

            string phoneId = $"phone{msgId}";
            string messageId = $"msg{msgId++}";
            string waUrl = $"https://web.whatsapp.com/send?phone=351{phone}&text={messageEncoded}";

            sb.AppendLine("<tr>");
            sb.AppendLine($"<td>{WebUtility.HtmlEncode(name)}</td>");
            sb.AppendLine($"<td><span id=\"{phoneId}\">{phone}</span><button onclick=\"copyToClipboard('{phoneId}')\">Copiar</button></td>");
            sb.AppendLine($"<td><span id=\"{messageId}\">{messageHtml}</span><button onclick=\"copyToClipboard('{messageId}')\">Copiar</button><a href='{waUrl}' target='_blank' data-wa='true'><button>Enviar</button></a></td>");
            sb.AppendLine("</tr>");
        }

        sb.AppendLine("</table>");
        sb.AppendLine("<div id=\"copy-toast\">Copiado!</div>");
        sb.AppendLine("<script>");
        sb.AppendLine("function copyToClipboard(id) {");
        sb.AppendLine("  const temp = document.createElement('textarea');");
        sb.AppendLine("  temp.value = document.getElementById(id).innerText;");
        sb.AppendLine("  document.body.appendChild(temp);");
        sb.AppendLine("  temp.select();");
        sb.AppendLine("  document.execCommand('copy');");
        sb.AppendLine("  document.body.removeChild(temp);");
        sb.AppendLine("  const toast = document.getElementById('copy-toast');");
        sb.AppendLine("  toast.style.display = 'block';");
        sb.AppendLine("  setTimeout(() => { toast.style.display = 'none'; }, 2000);");
        sb.AppendLine("}");
        sb.AppendLine("</script>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        File.WriteAllText(outputHtmlPath, sb.ToString(), Encoding.UTF8);
        Console.WriteLine($"✅ HTML com mensagens agrupadas gerado em: {outputHtmlPath}");
        OpenFileInDefaultApp(outputHtmlPath);
    }


    public static void OpenFileInDefaultApp(string filePath)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo("cmd", $"/c start \"\" \"{filePath}\"") { CreateNoWindow = true });
            }
            else if (OperatingSystem.IsMacOS())
            {
                Process.Start("open", filePath);
            }
            else
            {
                Console.WriteLine("⚠️ Sistema operacional não reconhecido.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ Não foi possível abrir o arquivo automaticamente: {ex.Message}");
        }
    }
}


