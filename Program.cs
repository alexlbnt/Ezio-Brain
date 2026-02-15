using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Speech.Recognition; 
using System.Speech.Synthesis;   
using System.Threading;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Net.Http;
using System.IO; 

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.Urls.Add("http://0.0.0.0:5000");

// ====================================================================================
//                          CONFIGURAÇÃO
// ====================================================================================

const string API_KEY = "AIzaSyAX8ds-Qqe9IlfII6TgxuswjVe1jZiMl-o"; 
const string AI_MODEL = "gemini-2.5-flash"; 
const string AI_URL = $"https://generativelanguage.googleapis.com/v1beta/models/{AI_MODEL}:generateContent?key={API_KEY}";

string caminhoMemoria = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "ezio_memoria.txt");

// ================= SENSORES =================
var cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
var ramCounter = new PerformanceCounter("Memory", "Available MBytes");
cpuCounter.NextValue(); 

var lastVoiceInteraction = DateTime.MinValue; 

// ================= FERRAMENTAS WINDOWS =================
[DllImport("user32.dll")] static extern bool LockWorkStation();
[DllImport("user32.dll")] static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, int dwExtraInfo);

const int VK_VOLUME_MUTE = 0xAD;
const int VK_VOLUME_UP = 0xAF;
const int VK_VOLUME_DOWN = 0xAE;
const int VK_MEDIA_NEXT_TRACK = 0xB0;
const int VK_MEDIA_PLAY_PAUSE = 0xB3;
const int KEYEVENTF_KEYUP = 0x0002;

// ====================================================================================
//                          NOVA FUNÇÃO: LER SPOTIFY (BLINDADA)
// ====================================================================================
string GetSpotifyTrack()
{
    try 
    {
        // Método 1: Busca direta pelo processo Spotify com título de janela
        var spotifyProc = Process.GetProcessesByName("Spotify")
                                 .FirstOrDefault(p => !string.IsNullOrEmpty(p.MainWindowTitle));

        if (spotifyProc != null)
        {
            string title = spotifyProc.MainWindowTitle;
            // Se o título tem um traço (Ex: "Queen - Bohemian Rhapsody"), é música.
            // Se for só "Spotify" ou "Spotify Free", está pausado.
            if (title.Contains("-")) 
            {
                return title; 
            }
            return "Spotify Pausado";
        }

        // Método 2 (Backup): Varredura geral (caso o nome do processo seja diferente)
        foreach (var p in Process.GetProcesses())
        {
            if (!string.IsNullOrEmpty(p.MainWindowTitle))
            {
                // Títulos de música geralmente têm " - " e o processo costuma ter "spotify" no nome
                if (p.MainWindowTitle.Contains("-") && 
                   (p.ProcessName.ToLower().Contains("spotify")))
                {
                    return p.MainWindowTitle;
                }
            }
        }
    } 
    catch {}
    
    return "Nenhuma Música";
}

// ====================================================================================
//                                 OUVIDO DO EZIO
// ====================================================================================

if (OperatingSystem.IsWindows())
{
    Thread speechThread = new Thread(() =>
    {
        try
        {
            using var recognizer = new SpeechRecognitionEngine();

            Choices comandos = new Choices();
            comandos.Add(new string[] { 
                // --- COMANDOS DE INÍCIO ---
                "ezio wake up", "ezio initialize", "ezio system online", "ezio protocol alpha",
                
                // --- COMANDOS PADRÃO ---
                "ezio lock system", "ezio status", "ezio weather",
                "ezio take a note", "ezio read notes", "ezio clear notes",
                "ezio read this",
                "ezio silence", "ezio volume up", "ezio volume down",
                "ezio next song", "ezio stop music", "ezio play music",
                "ezio open chrome", "ezio open spotify", "ezio open code", "ezio open calculator",
                "ezio who are you"
            });
            
            Grammar grammarComandos = new Grammar(new GrammarBuilder(comandos));
            grammarComandos.Name = "comandos"; 
            recognizer.LoadGrammar(grammarComandos);

            Grammar grammarDitado = new DictationGrammar();
            grammarDitado.Name = "ditado";
            grammarDitado.Enabled = false; 
            recognizer.LoadGrammar(grammarDitado);

            recognizer.SpeechRecognized += async (s, e) =>
            {
                lastVoiceInteraction = DateTime.Now;

                string texto = e.Result.Text.ToLower();
                float confianca = e.Result.Confidence;
                string grammarName = e.Result.Grammar.Name;

                if (grammarName == "ditado") {
                    try { File.AppendAllText(caminhoMemoria, $"- {texto} ({DateTime.Now:dd/MM HH:mm})\n"); Responder("Anotado."); } 
                    catch { Responder("Erro no arquivo."); }
                    grammarDitado.Enabled = false; grammarComandos.Enabled = true; return;
                }

                if (confianca < 0.5) return; 
                Console.WriteLine($"[EZIO] Comando: '{texto}'");

                // ==========================================================
                //                 LÓGICA DOS COMANDOS
                // ==========================================================
                
                if (texto.Contains("wake up") || texto.Contains("initialize") || texto.Contains("system online") || texto.Contains("protocol alpha"))
                {
                     var now = DateTime.Now;
                     var cpu = (int)cpuCounter.NextValue();
                     string dadosClima = await PegarClimaGoiania();
                     
                     string prompt = $@"
                        O Alex acordou o sistema às {now:HH:mm}. 
                        Status: CPU {cpu}%. Clima Goiânia: {dadosClima}.
                        Instrução: Aja como JARVIS. Seja fluido, natural e levemente sarcástico.
                      ";
                     
                     string resp = await PerguntarAoGemini(prompt);
                     Responder(resp);
                }
                else if (texto.Contains("ezio weather"))
                {
                    string dadosClima = await PegarClimaGoiania();
                    string resp = await PerguntarAoGemini($"O usuário quer saber do tempo (Goiânia: {dadosClima}). Fale de forma descontraída.");
                    Responder(resp);
                }
                else if (texto.Contains("read this")) {
                    string conteudo = PegarTextoClipboard(); 
                    if (string.IsNullOrWhiteSpace(conteudo)) Responder("Nada copiado.");
                    else {
                         if(conteudo.Length > 2000) conteudo = conteudo.Substring(0,2000);
                         string analise = await PerguntarAoGemini($"Resuma este texto de forma coloquial e fluida em Português: '{conteudo}'");
                         Responder(analise);
                    }
                }
                else if (texto.Contains("take a note")) { Responder("Pode ditar em inglês."); grammarComandos.Enabled = false; grammarDitado.Enabled = true; }
                else if (texto.Contains("read notes")) {
                    if (File.Exists(caminhoMemoria)) {
                        string notas = File.ReadAllText(caminhoMemoria);
                        if(string.IsNullOrWhiteSpace(notas)) Responder("Vazio.");
                        else { string resumo = await PerguntarAoGemini($"Leia essas notas de forma natural, como uma conversa: '{notas}'"); Responder(resumo); }
                    } else Responder("Vazio.");
                }
                else if (texto.Contains("clear notes")) { File.WriteAllText(caminhoMemoria, ""); Responder("Limpo."); }
                else if (texto.Contains("status")) {
                     var cpu = (int)cpuCounter.NextValue();
                     string frase = await PerguntarAoGemini($"CPU {cpu}%. Dê um status report rápido e fluido.");
                     Responder(frase);
                }
                else if (texto.Contains("lock system")) { Responder("Iniciando bloqueio."); LockWorkStation(); }
                else if (texto.Contains("who are you")) { string r = await PerguntarAoGemini("Quem é você? Responda curto e natural."); Responder(r); }
                
                // RÁPIDOS
                else if (texto.Contains("volume up")) for(int i=0; i<5; i++) keybd_event(VK_VOLUME_UP, 0, 0, 0);
                else if (texto.Contains("volume down")) for(int i=0; i<5; i++) keybd_event(VK_VOLUME_DOWN, 0, 0, 0);
                else if (texto.Contains("next song")) { keybd_event(VK_MEDIA_NEXT_TRACK, 0, 0, 0); keybd_event(VK_MEDIA_NEXT_TRACK, 0, KEYEVENTF_KEYUP, 0); }
                else if (texto.Contains("stop music") || texto.Contains("play music")) { keybd_event(VK_MEDIA_PLAY_PAUSE, 0, 0, 0); keybd_event(VK_MEDIA_PLAY_PAUSE, 0, KEYEVENTF_KEYUP, 0); }
                else if (texto.Contains("silence")) { keybd_event(VK_VOLUME_MUTE, 0, 0, 0); keybd_event(VK_VOLUME_MUTE, 0, KEYEVENTF_KEYUP, 0); }
                else if (texto.Contains("open chrome")) { Responder("Chrome."); Process.Start("explorer", "chrome.exe"); }
                else if (texto.Contains("open spotify")) { Responder("Spotify."); Process.Start("explorer", "spotify.exe"); }
                else if (texto.Contains("open code")) { Responder("VS Code."); try { Process.Start("explorer", "code"); } catch{} }
                else if (texto.Contains("open calculator")) Process.Start("calc.exe");
            };

            recognizer.SetInputToDefaultAudioDevice();
            recognizer.RecognizeAsync(RecognizeMode.Multiple);
            
            Console.WriteLine("===========================================");
            Console.WriteLine("                EZIO ONLINE                ");
            Console.WriteLine("             Diga: 'Ezio Wake Up'          ");
            Console.WriteLine("===========================================");
            
            while (true) { Thread.Sleep(1000); }
        }
        catch (Exception ex) { Console.WriteLine($"ERRO CRÍTICO: {ex.Message}"); }
    });
    
    speechThread.IsBackground = true;
    speechThread.Start();
}

async Task<string> PegarClimaGoiania()
{
    try 
    {
        using var client = new HttpClient();
        string url = "https://api.open-meteo.com/v1/forecast?latitude=-16.6869&longitude=-49.2648&current_weather=true";
        var json = await client.GetStringAsync(url);
        using var doc = JsonDocument.Parse(json);
        var current = doc.RootElement.GetProperty("current_weather");
        double temp = current.GetProperty("temperature").GetDouble();
        return $"{temp}°C";
    }
    catch { return "sem dados"; }
}

string PegarTextoClipboard()
{
    try {
        var psi = new ProcessStartInfo { FileName = "powershell", Arguments = "-NoProfile -Command \"Get-Clipboard | Out-String\"", RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        using var process = Process.Start(psi);
        if (!process.WaitForExit(3000)) { process.Kill(); return ""; }
        return process.StandardOutput.ReadToEnd().Trim();
    } catch { return ""; }
}

async Task<string> PerguntarAoGemini(string promptDoUsuario)
{
    if (API_KEY.Contains("COLOQUE_SUA_CHAVE")) return "Configure a chave.";
    try {
        using var client = new HttpClient();
        string promptFinal = @"SYSTEM: Você é o Ezio, IA do Alex. Responda em Português do Brasil. Fale de forma FLUIDA, como uma pessoa real, não como um robô. Use frases curtas. Não use asteriscos.";
        
        var requestData = new { contents = new[] { new { parts = new[] { new { text = $"{promptFinal}\nCONTEXTO: {promptDoUsuario}" } } } } };
        var jsonContent = new StringContent(JsonSerializer.Serialize(requestData), Encoding.UTF8, "application/json");
        var response = await client.PostAsync(AI_URL, jsonContent);
        if (response.IsSuccessStatusCode) {
            var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (doc.RootElement.TryGetProperty("candidates", out var candidates)) return candidates[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString();
        } else if ((int)response.StatusCode == 429) return "Estou processando muita informação. Aguarde.";
    } catch {} return "Erro de conexão."; 
}

void Responder(string texto)
{
    Task.Run(() => {
        try {
            using var synth = new SpeechSynthesizer();
            synth.SetOutputToDefaultAudioDevice();
            
            synth.Rate = 2; // Voz Acelerada

            var vozes = synth.GetInstalledVoices();
            var voz = vozes.FirstOrDefault(v => v.VoiceInfo.Name.Contains("Daniel")) ?? vozes.FirstOrDefault(v => v.VoiceInfo.Culture.Name == "pt-BR" && v.VoiceInfo.Gender == VoiceGender.Male);
            if (voz != null) synth.SelectVoice(voz.VoiceInfo.Name);
            
            synth.Speak(texto);
        } catch {}
    });
}

// ====================================================================================
//                          APIS PARA O RELÓGIO (ARDUINO)
// ====================================================================================

app.MapGet("/api/status", () => 
{
    // Verifica se houve interação nos últimos 10 segundos
    bool active = (DateTime.Now - lastVoiceInteraction).TotalSeconds < 10;
    
    // Pega a música atual
    string song = GetSpotifyTrack();

    return Results.Json(new { 
        cpu_usage = (int)cpuCounter.NextValue(), 
        ram_free_mb = (int)ramCounter.NextValue(), 
        uptime = DateTime.Now.ToString("HH:mm:ss"),
        interaction_active = active,
        current_song = song // <--- ENVIADO PRO RELÓGIO
    });
});

// Endpoint para passar música pelo relógio
app.MapPost("/api/next", () => {
    keybd_event(VK_MEDIA_NEXT_TRACK, 0, 0, 0);
    keybd_event(VK_MEDIA_NEXT_TRACK, 0, KEYEVENTF_KEYUP, 0);
    return Results.Ok();
});

// Endpoint para Play/Pause
app.MapPost("/api/playpause", () => {
    keybd_event(VK_MEDIA_PLAY_PAUSE, 0, 0, 0);
    keybd_event(VK_MEDIA_PLAY_PAUSE, 0, KEYEVENTF_KEYUP, 0);
    return Results.Ok();
});

app.Run();