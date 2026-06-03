using System.Collections.Concurrent;
using System.Security.Authentication;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WebSocketSharp;

namespace ChzzkChatTester;

// ═══════════════════════════════════════════════════════════════
//  Chzzk 채팅 연동 실시간 테스트 콘솔
//  게임 커맨드 감지, 쿨타임, 큐 상태를 실시간으로 확인
// ═══════════════════════════════════════════════════════════════

class Program
{
    // ── Chzzk API 엔드포인트 ─────────────────────────────────────
    private const string WSS_URL           = "wss://kr-ss3.chat.naver.com/chat";
    private const string HEARTBEAT_REQ     = "{\"ver\":\"2\",\"cmd\":0}";
    private const string HEARTBEAT_RESP    = "{\"ver\":\"2\",\"cmd\":10000}";
    private const float  COOLDOWN_SECONDS  = 30f;

    // ── 채널 ID (여기를 변경) ─────────────────────────────────────
    private const string CHANNEL_ID = "75b97045264eab24dc4df59db78d29e4";

    // ── 게임 커맨드 정의 ──────────────────────────────────────────
    private static readonly Dictionary<string, (string emoji, string label, ConsoleColor color)> Commands = new()
    {
        { "!heal",    ("❤️ ", "HEAL   체력 30% 회복",      ConsoleColor.Green)       },
        { "!힐",      ("❤️ ", "HEAL   체력 30% 회복",      ConsoleColor.Green)       },
        { "!buff",    ("⚡",  "BUFF   랜덤 버프 30초",     ConsoleColor.Yellow)      },
        { "!버프",    ("⚡",  "BUFF   랜덤 버프 30초",     ConsoleColor.Yellow)      },
        { "!item",    ("🎁",  "ITEM   랜덤 아이템 드롭",   ConsoleColor.Cyan)        },
        { "!아이템",  ("🎁",  "ITEM   랜덤 아이템 드롭",   ConsoleColor.Cyan)        },
        { "!skull",   ("💀",  "SKULL  랜덤 해골 드롭",     ConsoleColor.Magenta)     },
        { "!해골",    ("💀",  "SKULL  랜덤 해골 드롭",     ConsoleColor.Magenta)     },
        { "!omen",    ("🌑",  "OMEN   흉조 상자 강제 스폰", ConsoleColor.DarkMagenta) },
        { "!흉조",    ("🌑",  "OMEN   흉조 상자 강제 스폰", ConsoleColor.DarkMagenta) },
        { "!curse",   ("☠️ ", "CURSE  플레이어 저주",       ConsoleColor.DarkRed)     },
        { "!저주",    ("☠️ ", "CURSE  플레이어 저주",       ConsoleColor.DarkRed)     },
        { "!boss",    ("👹",  "BOSS   적 강화",             ConsoleColor.Red)         },
        { "!보스",    ("👹",  "BOSS   적 강화",             ConsoleColor.Red)         },
    };

    // ── 상태 변수 ─────────────────────────────────────────────────
    private static WebSocket?               _socket;
    private static DateTime                 _cooldownUntil = DateTime.MinValue;
    private static bool                     _cooldownLogged = true;
    private static readonly ConcurrentQueue<(string nick, string cmd, string label, ConsoleColor color, string emoji)>
                                            _cmdQueue      = new();
    private static int                      _totalMessages = 0;
    private static int                      _cmdCount      = 0;
    private static readonly object          _logLock       = new();
    private static bool                     _running       = true;

    // ── Main ──────────────────────────────────────────────────────
    static async Task Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        try { Console.CursorVisible = false; } catch { }

        PrintBanner();

        string channelId = CHANNEL_ID;
        if (args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]))
        {
            channelId = args[0];
            Log($"[설정] 채널 ID 인수 사용: {channelId}", ConsoleColor.Cyan);
        }

        Log("Chzzk 채팅 채널 정보 가져오는 중...", ConsoleColor.Gray);

        try
        {
            (string cid, string token) = await GetChatCredentials(channelId);
            Log($"[✓] 채널 채팅 ID: {cid[..Math.Min(8, cid.Length)]}...", ConsoleColor.Green);
            Log("[✓] 액세스 토큰 획득 완료", ConsoleColor.Green);

            ConnectWebSocket(cid, token);

            // 큐 처리 루프 (백그라운드)
            _ = Task.Run(ProcessCommandQueue);

            // 상태 표시 루프
            _ = Task.Run(StatusLoop);

            Log("", ConsoleColor.White);
            Log("═══════════════════════════════════════════════════════", ConsoleColor.DarkGray);
            Log("  채팅을 입력하면 실시간으로 감지됩니다.  종료: Ctrl+C", ConsoleColor.White);
            Log("═══════════════════════════════════════════════════════", ConsoleColor.DarkGray);
            Log("", ConsoleColor.White);

            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                _running = false;
                Log("\n[종료] 연결을 끊는 중...", ConsoleColor.Yellow);
                _socket?.Close();
            };

            while (_running)
                await Task.Delay(200);
        }
        catch (Exception ex)
        {
            Log($"[오류] {ex.Message}", ConsoleColor.Red);
            Log("[힌트] 방송이 실행 중인지, 채널 ID가 올바른지 확인하세요.", ConsoleColor.DarkYellow);
        }

        Log("[종료] 프로그램을 종료합니다.", ConsoleColor.Gray);
    }

    // ── Chzzk API: 채팅 자격증명 획득 ────────────────────────────
    static async Task<(string cid, string token)> GetChatCredentials(string channelId)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0");

        // 1. 라이브 상태에서 chatChannelId 획득
        var liveResp = await http.GetStringAsync(
            $"https://api.chzzk.naver.com/polling/v2/channels/{channelId}/live-status");
        var liveJson = JObject.Parse(liveResp);
        string cid   = liveJson["content"]?["chatChannelId"]?.ToString()
                       ?? throw new Exception("chatChannelId를 찾을 수 없습니다. 방송이 켜져있는지 확인하세요.");

        // 2. 액세스 토큰 획득
        var tokenResp = await http.GetStringAsync(
            $"https://comm-api.game.naver.com/nng_main/v1/chats/access-token?channelId={cid}&chatType=STREAMING");
        var tokenJson = JObject.Parse(tokenResp);
        string token  = tokenJson["content"]?["accessToken"]?.ToString()
                        ?? throw new Exception("accessToken을 찾을 수 없습니다.");

        return (cid, token);
    }

    // ── WebSocket 연결 ────────────────────────────────────────────
    static void ConnectWebSocket(string cid, string token)
    {
        _socket = new WebSocket(WSS_URL);

        // WSS SSL 설정
        _socket.SslConfiguration.EnabledSslProtocols =
            SslProtocols.Tls12 | SslProtocols.Tls11 | SslProtocols.Tls;

        _socket.OnOpen    += (_, _) => OnOpen(cid, token);
        _socket.OnMessage += (_, e)  => OnMessage(e.Data);
        _socket.OnClose   += (_, e)  => Log($"[연결 종료] {e.Reason} (코드:{e.Code})", ConsoleColor.DarkYellow);
        _socket.OnError   += (_, e)  => Log($"[소켓 오류] {e.Message}", ConsoleColor.Red);
        _socket.Connect();
    }

    static void OnOpen(string cid, string token)
    {
        Log("[✓] WebSocket 연결됨! 채팅 채널에 참가 중...", ConsoleColor.Green);
        string joinMsg = $"{{\"ver\":\"2\",\"cmd\":100,\"svcid\":\"game\",\"cid\":\"{cid}\"," +
                         $"\"bdy\":{{\"uid\":null,\"devType\":2001,\"accTkn\":\"{token}\",\"auth\":\"READ\"}},\"tid\":1}}";
        _socket!.Send(joinMsg);
    }

    static void OnMessage(string data)
    {
        try
        {
            var json = JsonConvert.DeserializeObject<IDictionary<string, object>>(data);
            if (json == null) return;

            long cmd = Convert.ToInt64(json["cmd"]);

            switch (cmd)
            {
                case 0: // 서버 하트비트 요청
                    _socket?.Send(HEARTBEAT_RESP);
                    break;

                case 10000: // 하트비트 응답
                    break;

                case 10100: // 채널 참가 성공
                    Log("[✓] 채팅 채널 참가 완료! 채팅을 기다리는 중...", ConsoleColor.Green);
                    break;

                case 93101: // 일반 채팅
                    ParseChatMessages((JArray)json["bdy"]);
                    break;

                default:
                    break;
            }
        }
        catch (Exception ex)
        {
            Log($"[파싱 오류] {ex.Message}", ConsoleColor.DarkRed);
        }
    }

    static void ParseChatMessages(JArray body)
    {
        foreach (JToken token in body)
        {
            try
            {
                var obj = (JObject)token;
                string? msg     = obj["msg"]?.ToString().Trim();
                string? profRaw = obj["profile"]?.ToString();

                if (string.IsNullOrWhiteSpace(msg)) continue;

                string nickname = "익명";
                if (!string.IsNullOrEmpty(profRaw))
                {
                    try
                    {
                        var prof = JObject.Parse(profRaw);
                        nickname = prof["nickname"]?.ToString() ?? "익명";
                    }
                    catch (Exception ex)
                    {
                        Log($"[프로필 파싱 오류] {ex.Message}. 익명으로 처리합니다. 원본: {profRaw}", ConsoleColor.DarkRed);
                    }
                }

                Interlocked.Increment(ref _totalMessages);

                string lower = msg.ToLower();
                if (Commands.TryGetValue(lower, out var cmdInfo))
                {
                    Interlocked.Increment(ref _cmdCount);

                    int pendingCount = _cmdQueue.Count;
                    double waitTime = 0;
                    double cooldownRemain = (_cooldownUntil - DateTime.Now).TotalSeconds;
                    if (cooldownRemain > 0)
                    {
                        waitTime = cooldownRemain + (pendingCount * COOLDOWN_SECONDS);
                    }
                    else
                    {
                        waitTime = pendingCount * COOLDOWN_SECONDS;
                    }

                    _cmdQueue.Enqueue((nickname, lower, cmdInfo.label, cmdInfo.color, cmdInfo.emoji));
                    LogCommandReceived(nickname, lower, cmdInfo.label, cmdInfo.color, cmdInfo.emoji, waitTime, pendingCount + 1);
                }
                else
                {
                    // 일반 채팅
                    LogChat(nickname, msg);
                }
            }
            catch (Exception ex)
            {
                Log($"[채팅 메시지 개별 처리 오류] {ex.Message}", ConsoleColor.Red);
            }
        }
    }

    // ── 커맨드 큐 처리 (쿨타임 포함) ────────────────────────────
    static async Task ProcessCommandQueue()
    {
        while (_running)
        {
            bool isOnCooldown = DateTime.Now < _cooldownUntil;
            if (!isOnCooldown)
            {
                if (!_cooldownLogged)
                {
                    Log("글로벌 쿨타임이 종료되었습니다. 다음 커맨드가 즉시 사용 가능합니다.", ConsoleColor.Green);
                    _cooldownLogged = true;
                }

                if (_cmdQueue.TryDequeue(out var item))
                {
                    _cooldownUntil = DateTime.Now.AddSeconds(COOLDOWN_SECONDS);
                    _cooldownLogged = false; // 새로운 쿨타임 시작
                    LogCommandExecuted(item.nick, item.cmd, item.label, item.color, item.emoji);
                }
            }
            await Task.Delay(100);
        }
    }

    // ── 상태 표시 루프 ─────────────────────────────────────────
    static async Task StatusLoop()
    {
        while (_running)
        {
            await Task.Delay(1000);
            double cooldownRemain = (double)(_cooldownUntil - DateTime.Now).TotalSeconds;
            string cooldownStr    = cooldownRemain > 0
                ? $"쿨타임 {cooldownRemain:F0}초"
                : "대기 중";
            int queueCount        = _cmdQueue.Count;

            lock (_logLock)
            {
                try
                {
                    int prevLeft = Console.CursorLeft, prevTop = Console.CursorTop;
                    Console.SetCursorPosition(0, 2); // 상태바 위치 (고정 라인)
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write(
                        $" 📡 연결됨  |  💬 메시지: {_totalMessages,5}  |  🎮 커맨드: {_cmdCount,4}  |  큐: {queueCount,2}  |  {cooldownStr,-15}    ");
                    Console.ResetColor();
                    Console.SetCursorPosition(prevLeft, prevTop);
                }
                catch
                {
                    // 리다이렉트 환경에서는 상태바 업데이트 무시
                }
            }
        }
    }

    // ── 로깅 유틸 ─────────────────────────────────────────────
    static void PrintBanner()
    {
        try { Console.Clear(); } catch { }
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine();
        Console.WriteLine("  ██████╗██╗  ██╗███████╗███████╗██╗  ██╗");
        Console.WriteLine("  ██╔════╝██║  ██║╚════███╗╚════███╗██║ ██╔╝");
        Console.WriteLine("  ██║     ███████║    ███╔╝    ███╔╝█████╔╝ ");
        Console.WriteLine("  ██║     ██╔══██║   ███╔╝    ███╔╝ ██╔═██╗ ");
        Console.WriteLine("  ╚██████╗██║  ██║███████╗███████╗██║  ██╗");
        Console.WriteLine("   ╚═════╝╚═╝  ╚═╝╚══════╝╚══════╝╚═╝  ╚═╝");
        Console.ResetColor();
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("          채팅 연동 실시간 테스트 콘솔 v1.0");
        Console.ResetColor();
        Console.WriteLine();
        // 상태바 자리 예약
        Console.WriteLine("  [상태 로딩 중...]");
        Console.WriteLine();
    }

    static void Log(string msg, ConsoleColor color = ConsoleColor.White)
    {
        lock (_logLock)
        {
            string time = DateTime.Now.ToString("HH:mm:ss");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($"  [{time}] ");
            Console.ForegroundColor = color;
            Console.WriteLine(msg);
            Console.ResetColor();
        }
    }

    static void LogChat(string nickname, string message)
    {
        lock (_logLock)
        {
            string time = DateTime.Now.ToString("HH:mm:ss");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($"  [{time}] ");
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.Write($"{nickname}");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write(": ");
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine(message);
            Console.ResetColor();
        }
    }

    static void LogCommandReceived(string nickname, string cmd, string label, ConsoleColor color, string emoji, double waitTime, int queuePosition)
    {
        lock (_logLock)
        {
            string time = DateTime.Now.ToString("HH:mm:ss");

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("  ┌──────────────────────────────────────────────────");
            Console.Write($"  │ [{time}] [커맨드 감지] ");
            Console.ForegroundColor = color;
            Console.Write($"{emoji} {label} ");
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write("← ");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write(nickname);
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($" ({cmd})");
            Console.WriteLine();
            
            if (waitTime <= 0)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("  │   🟢 즉시 실행 가능 (대기 큐 없음)");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine($"  │   ⏳ 대기 중: 약 {waitTime:F1}초 후 실행 가능 (대기열 {queuePosition}번째)");
            }
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("  └──────────────────────────────────────────────────");
            Console.ResetColor();
        }
    }

    static void LogCommandExecuted(string nickname, string cmd, string label, ConsoleColor color, string emoji)
    {
        lock (_logLock)
        {
            string time = DateTime.Now.ToString("HH:mm:ss");

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("  ┌──────────────────────────────────────────────────");
            Console.Write($"  │ [{time}] [커맨드 실행] ");
            Console.ForegroundColor = color;
            Console.Write($"{emoji} {label} ");
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write("실행 완료!");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write($" (by {nickname})");
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"  │   ✅ 실행됨 → {COOLDOWN_SECONDS}초 글로벌 쿨타임 시작");
            Console.WriteLine("  └──────────────────────────────────────────────────");
            Console.ResetColor();
        }
    }
}
