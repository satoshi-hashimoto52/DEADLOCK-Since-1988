using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace Deadlock.Economy
{
    /// <summary>
    /// The game's side of the account and economy service (Stage 37D): asks, and shows only what the service confirms.
    /// A request that gets no answer (timeout, connection lost) is sent again with the same request id, so the service
    /// applies it once and answers with the saved result. When the service cannot be reached the answer says so
    /// (status "error", code "unavailable"); the local Stage 34 data is never used in its place. Signing in is the
    /// development sign-in only (official sign-in is not decided, D-072).
    /// </summary>
    public sealed class EconomyClient : IDisposable
    {
        readonly HttpClient http;
        readonly string baseUrl;

        public string AccountId { get; }
        /// <summary>Extra sends of a request that got no answer (same request id).</summary>
        public int Retries = 2;
        public int RetryDelayMs = 300;
        /// <summary>Sends made (tests: a lost answer shows as two sends of one request).</summary>
        public int Sends;

        public EconomyClient(string baseUrl, string accountId, string token, TimeSpan? timeout = null)
        {
            this.baseUrl = baseUrl.TrimEnd('/');
            AccountId = accountId;
            http = new HttpClient { Timeout = timeout ?? TimeSpan.FromSeconds(10) };
            if (!string.IsNullOrEmpty(token))
                http.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
        }

        public void Dispose() => http.Dispose();

        static T Unreachable<T>(string detail) where T : new()
        {
            var a = new T();
            var t = typeof(T);
            t.GetField("status").SetValue(a, EconomyCodes.Error);
            t.GetField("code").SetValue(a, EconomyCodes.Unavailable);
            t.GetField("message").SetValue(a, "サーバーに接続できません。残高と所持品はサーバーの確定値だけを表示します（" + detail + "）");
            return a;
        }

        async Task<T> Send<T>(HttpMethod method, string path, object body, bool retry) where T : new()
        {
            string json = body != null ? JsonUtility.ToJson(body) : null;
            string last = "";
            for (int attempt = 0; attempt <= (retry ? Retries : 0); attempt++)
            {
                if (attempt > 0)
                    await Task.Delay(RetryDelayMs * attempt).ConfigureAwait(false);
                try
                {
                    using var msg = new HttpRequestMessage(method, baseUrl + path);
                    if (json != null)
                        msg.Content = new StringContent(json, Encoding.UTF8, "application/json");
                    Sends++;
                    using var response = await http.SendAsync(msg).ConfigureAwait(false);
                    string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var answer = JsonUtility.FromJson<T>(text);
                    if (answer == null)
                        throw new FormatException("empty answer");
                    // 503: nothing was changed, the same request may go again.
                    if ((int)response.StatusCode == 503 && attempt < (retry ? Retries : 0))
                    {
                        last = "503";
                        continue;
                    }
                    return answer;
                }
                catch (Exception e) when (e is HttpRequestException || e is TaskCanceledException || e is FormatException || e is ArgumentException || e is System.IO.IOException)
                {
                    last = e.GetBaseException().GetType().Name;
                }
            }
            return Unreachable<T>(last);
        }

        string Account(string rest) => EconomyProtocol.AccountPath(AccountId, rest);

        public Task<WalletAnswer> Wallet() => Send<WalletAnswer>(HttpMethod.Get, Account("wallet"), null, true);

        /// <summary>A draw ("single" or "ten") under a request id the caller keeps until it has a confirmed answer.</summary>
        public Task<DrawAnswer> Draw(string kind, string requestId) =>
            Send<DrawAnswer>(HttpMethod.Post, Account("gacha/draw"), new DrawRequest { requestId = requestId, kind = kind, accountId = AccountId }, true);

        /// <summary>The saved result of a draw (after a lost answer).</summary>
        public Task<DrawAnswer> FindDraw(string requestId) => Send<DrawAnswer>(HttpMethod.Get, Account("gacha/draws/" + requestId), null, true);

        public Task<ShopAnswer> Shop() => Send<ShopAnswer>(HttpMethod.Get, Account("shop"), null, true);

        public Task<HistoryAnswer> History(int limit = 100) => Send<HistoryAnswer>(HttpMethod.Get, Account("gacha/history?limit=" + limit), null, true);

        public Task<SimpleAnswer> MarkRevealed(string requestId) =>
            Send<SimpleAnswer>(HttpMethod.Post, Account("gacha/reveal"), new DrawRequest { requestId = requestId, kind = "", accountId = AccountId }, true);

        public Task<ExchangeAnswer> Exchange(string itemId, string requestId) =>
            Send<ExchangeAnswer>(HttpMethod.Post, Account("exchange"), new ExchangeRequest { requestId = requestId, itemId = itemId, accountId = AccountId }, true);

        public Task<JoinTicketAnswer> JoinTicket() => Send<JoinTicketAnswer>(HttpMethod.Post, Account("join-ticket"), new SimpleAnswer(), true);

#if UNITY_EDITOR || DEVELOPMENT_BUILD || DEADLOCK_ECONOMY_DEV
        /// <summary>Development sign-in (only a service started with development sign-in answers).</summary>
        public static async Task<DevSessionAnswer> DevSession(string baseUrl, string accountId, string displayName = null)
        {
            using var c = new EconomyClient(baseUrl, null, null);
            return await c.Send<DevSessionAnswer>(HttpMethod.Post, EconomyProtocol.PathDevSession, new DevSessionRequest { accountId = accountId, displayName = displayName ?? accountId }, false).ConfigureAwait(false);
        }

        public Task<SimpleAnswer> DevGrantPoints(int points, string requestId) =>
            Send<SimpleAnswer>(HttpMethod.Post, EconomyProtocol.PathDevGrantPoints, new DevGrantRequest { accountId = AccountId, points = points, requestId = requestId }, true);
#endif
    }

    /// <summary>
    /// What a gacha screen in official online play would hold (Stage 37D): the balance and items as last confirmed by
    /// the service, and the request id of an action not yet confirmed, sent again as it was (a second tap or a retry
    /// after a lost answer does not make a second draw). Nothing here reads or writes the local Stage 34 data.
    /// The gacha screen uses it in development builds through the development sign-in (Deadlock.Dev's
    /// ServerGachaSource, Stage 37E); official sign-in is not decided (D-072).
    /// </summary>
    public sealed class OnlineGachaSession
    {
        readonly EconomyClient client;

        public WalletAnswer Wallet { get; private set; }
        /// <summary>The balance shown: the service's confirmed one, or unknown (never a local value).</summary>
        public bool WalletKnown => Wallet != null && Wallet.status == EconomyCodes.Ok;
        public string PendingDrawId { get; private set; }
        public string PendingDrawKind { get; private set; }
        public string PendingExchangeId { get; private set; }
        public string PendingExchangeItem { get; private set; }

        public OnlineGachaSession(EconomyClient client) => this.client = client;

        public EconomyClient Client => client;

        /// <summary>An action of before whose answer never came (kept outside the gacha data, Stage 37E): sent again as it was.</summary>
        public void Restore(string drawId, string drawKind, string exchangeId, string exchangeItem)
        {
            if (EconomyProtocol.ValidRequestId(drawId) && (drawKind == "single" || drawKind == "ten"))
            {
                PendingDrawId = drawId;
                PendingDrawKind = drawKind;
            }
            if (EconomyProtocol.ValidRequestId(exchangeId) && !string.IsNullOrEmpty(exchangeItem))
            {
                PendingExchangeId = exchangeId;
                PendingExchangeItem = exchangeItem;
            }
        }

        /// <summary>The saved result of the pending draw, if the service applied it (its answer was lost): then it is no longer pending.</summary>
        public async Task<DrawAnswer> ResolvePendingDraw()
        {
            if (PendingDrawId == null)
                return null;
            var found = await client.FindDraw(PendingDrawId);
            if (found.Confirmed)
            {
                PendingDrawId = null;
                PendingDrawKind = null;
                await Refresh();
            }
            return found;
        }

        public async Task<WalletAnswer> Refresh()
        {
            var w = await client.Wallet();
            if (w.status == EconomyCodes.Ok)
                Wallet = w;
            return w;
        }

        /// <summary>
        /// Draws, or (while an earlier draw has no confirmed answer) sends that same draw again. Another kind while one
        /// is pending is refused until the pending one is answered.
        /// </summary>
        public async Task<DrawAnswer> Draw(string kind, string requestId = null)
        {
            if (PendingDrawId != null && PendingDrawKind != kind)
                return new DrawAnswer { status = EconomyCodes.Refused, code = EconomyCodes.Conflict, message = "前のガチャの結果を確認しています（同じ種類で押すと、同じ取引として再送します）" };
            if (PendingDrawId == null)
            {
                PendingDrawId = EconomyProtocol.ValidRequestId(requestId) ? requestId : EconomyProtocol.NewRequestId();
                PendingDrawKind = kind;
            }
            var answer = await client.Draw(kind, PendingDrawId);
            if (answer.status != EconomyCodes.Error)
            {
                PendingDrawId = null; // answered (done, done before, or refused): a new tap is a new draw
                PendingDrawKind = null;
            }
            if (answer.Confirmed)
                await Refresh();
            return answer;
        }

        public async Task<ExchangeAnswer> Exchange(string itemId, string requestId = null)
        {
            if (PendingExchangeId != null && PendingExchangeItem != itemId)
                return new ExchangeAnswer { status = EconomyCodes.Refused, code = EconomyCodes.Conflict, message = "前の交換の結果を確認しています" };
            if (PendingExchangeId == null)
            {
                PendingExchangeId = EconomyProtocol.ValidRequestId(requestId) ? requestId : EconomyProtocol.NewRequestId();
                PendingExchangeItem = itemId;
            }
            var answer = await client.Exchange(itemId, PendingExchangeId);
            if (answer.status != EconomyCodes.Error)
            {
                PendingExchangeId = null;
                PendingExchangeItem = null;
            }
            if (answer.Confirmed)
                await Refresh();
            return answer;
        }
    }
}
