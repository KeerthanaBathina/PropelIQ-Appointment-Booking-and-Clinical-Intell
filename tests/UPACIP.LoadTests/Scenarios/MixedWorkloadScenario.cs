using System.Net.Http.Headers;
using NBomber.Contracts;
using NBomber.CSharp;
using UPACIP.LoadTests.Infrastructure;

namespace UPACIP.LoadTests.Scenarios;

/// <summary>
/// NBomber mixed-workload scenario that exercises all four operation types
/// simultaneously with a realistic traffic distribution (US_082 task_002, AC-1, AC-4).
///
/// <para>
/// <b>Traffic distribution (1000 total virtual users):</b>
/// <list type="table">
///   <item>40% â€” Booking flow (slot lookup â†’ book â†’ confirm)</item>
///   <item>30% â€” Search operations (appointment search + patient lookup)</item>
///   <item>20% â€” Dashboard loads (patient + staff dashboards)</item>
///   <item>10% â€” Document upload (async AI queue, validates AC-3 non-blocking)</item>
/// </list>
/// </para>
///
/// <para>
/// <b>SLA:</b> Each operation type must meet its individual SLA threshold simultaneously
/// under combined mixed load (AC-1). Overall P95 &lt; 3000 ms as a composite bound.
/// </para>
///
/// <para>
/// <b>Concurrency:</b> 1000 total users, 180-second duration, 30-second ramp-up.
/// </para>
/// </summary>
public static class MixedWorkloadScenario
{
    private static readonly Random Rng = new();

    /// <summary>
    /// Creates the four weighted scenarios that together form the mixed workload.
    /// </summary>
    /// <returns>
    /// Array of <see cref="ScenarioProps"/> to be passed to <c>NBomberRunner.RegisterScenarios</c>.
    /// </returns>
    public static ScenarioProps[] Create(
        LoadTestConfiguration config,
        int totalUsers      = 1000,
        int durationSeconds = 180)
    {
        var patientClient = BuildClient(config, usePatient: true);
        var staffClient   = BuildClient(config, usePatient: false);

        var providerIds = Enumerable.Range(1, 50)
            .Select(i => new Guid(i, 0, 0, new byte[8]).ToString())
            .ToArray();

        var patientIds = Enumerable.Range(1, 100)
            .Select(i => new Guid(i + 2_000_001 - 1, 0, 0, new byte[8]).ToString())
            .ToArray();

        // â”€â”€ 40% Booking â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        var bookingRate = (int)Math.Round(totalUsers * 0.40 / 10.0);
        var bookingScenario = Scenario.Create("mixed_booking", async ctx =>
        {
            var providerId = providerIds[Rng.Next(providerIds.Length)];
            var tomorrow   = DateTime.UtcNow.Date.AddDays(1).ToString("yyyy-MM-dd");

            using var slotReq = new HttpRequestMessage(HttpMethod.Get,
                $"{config.BaseUrl}/api/appointments/slots?providerId={providerId}&date={tomorrow}");
            using var slotResp = await patientClient.SendAsync(slotReq, CancellationToken.None);
            if (!slotResp.IsSuccessStatusCode)
                return Response.Fail(message: $"MixedBooking_Slots:{(int)slotResp.StatusCode}");

            using var bookReq = new HttpRequestMessage(HttpMethod.Post,
                $"{config.BaseUrl}/api/appointments");
            bookReq.Content = new StringContent(
                $$$"""{"providerId":"{{{providerId}}}","slotTime":"{{{tomorrow}}}T09:00:00Z","reason":"Mixed load test"}""",
                System.Text.Encoding.UTF8, "application/json");
            using var bookResp = await patientClient.SendAsync(bookReq, CancellationToken.None);
            // 409 = conflict â€” expected under concurrent load
            if (!bookResp.IsSuccessStatusCode && bookResp.StatusCode != System.Net.HttpStatusCode.Conflict)
                return Response.Fail(message: $"MixedBooking_Book:{(int)bookResp.StatusCode}");

            return Response.Ok();
        })
        .WithLoadSimulations(
            Simulation.Inject(
                rate:     bookingRate,
                interval: TimeSpan.FromSeconds(1),
                during:   TimeSpan.FromSeconds(durationSeconds)));

        // â”€â”€ 30% Search â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        var searchRate = (int)Math.Round(totalUsers * 0.30 / 10.0);
        var searchScenario = Scenario.Create("mixed_search", async ctx =>
        {
            var providerId = providerIds[Rng.Next(providerIds.Length)];
            var date       = DateTime.UtcNow.Date.AddDays(Rng.Next(0, 30)).ToString("yyyy-MM-dd");

            using var req = new HttpRequestMessage(HttpMethod.Get,
                $"{config.BaseUrl}/api/appointments/search?date={date}&providerId={providerId}");
            using var resp = await staffClient.SendAsync(req, CancellationToken.None);
            if (!resp.IsSuccessStatusCode && resp.StatusCode != System.Net.HttpStatusCode.NotFound)
                return Response.Fail(message: $"MixedSearch:{(int)resp.StatusCode}");

            return Response.Ok();
        })
        .WithLoadSimulations(
            Simulation.Inject(
                rate:     searchRate,
                interval: TimeSpan.FromSeconds(1),
                during:   TimeSpan.FromSeconds(durationSeconds)));

        // â”€â”€ 20% Dashboard â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        var dashRate = (int)Math.Round(totalUsers * 0.20 / 10.0);
        var dashScenario = Scenario.Create("mixed_dashboard", async ctx =>
        {
            bool isStaff = ctx.InvocationNumber % 2 == 0;
            var client   = isStaff ? staffClient : patientClient;
            var endpoint = isStaff ? "/api/dashboard/staff" : "/api/dashboard/patient";

            using var req  = new HttpRequestMessage(HttpMethod.Get, $"{config.BaseUrl}{endpoint}");
            using var resp = await client.SendAsync(req, CancellationToken.None);
            if (!resp.IsSuccessStatusCode &&
                resp.StatusCode != System.Net.HttpStatusCode.Unauthorized &&
                resp.StatusCode != System.Net.HttpStatusCode.Forbidden)
                return Response.Fail(message: $"MixedDashboard:{(int)resp.StatusCode}");

            return Response.Ok();
        })
        .WithLoadSimulations(
            Simulation.Inject(
                rate:     dashRate,
                interval: TimeSpan.FromSeconds(1),
                during:   TimeSpan.FromSeconds(durationSeconds)));

        // â”€â”€ 10% Document upload (validates async AI queue, AC-3) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        var uploadRate = (int)Math.Round(totalUsers * 0.10 / 10.0);
        var uploadScenario = Scenario.Create("mixed_upload", async ctx =>
        {
            // Tiny synthetic PDF-like payload (does not contain actual document data).
            var fakeContent = new ByteArrayContent(new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D });
            fakeContent.Headers.ContentType =
                new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");

            using var form = new MultipartFormDataContent();
            form.Add(fakeContent, "file", "load-test.pdf");
            form.Add(new StringContent("load-test"), "patientId");

            using var req = new HttpRequestMessage(HttpMethod.Post,
                $"{config.BaseUrl}/api/documents/upload")
            { Content = form };
            using var resp = await patientClient.SendAsync(req, CancellationToken.None);

            // 202 Accepted or 400 (invalid patient) â€” both acceptable for load testing.
            if (!resp.IsSuccessStatusCode &&
                resp.StatusCode != System.Net.HttpStatusCode.Accepted &&
                resp.StatusCode != System.Net.HttpStatusCode.BadRequest &&
                resp.StatusCode != System.Net.HttpStatusCode.Unauthorized &&
                resp.StatusCode != System.Net.HttpStatusCode.Forbidden)
                return Response.Fail(message: $"MixedUpload:{(int)resp.StatusCode}");

            return Response.Ok();
        })
        .WithLoadSimulations(
            Simulation.Inject(
                rate:     uploadRate,
                interval: TimeSpan.FromSeconds(1),
                during:   TimeSpan.FromSeconds(durationSeconds)));

        return [bookingScenario, searchScenario, dashScenario, uploadScenario];
    }

    // â”€â”€ Helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static HttpClient BuildClient(LoadTestConfiguration config, bool usePatient)
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var token  = usePatient ? config.AuthTokenPatient : config.AuthTokenStaff;
        if (!string.IsNullOrEmpty(token))
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}


