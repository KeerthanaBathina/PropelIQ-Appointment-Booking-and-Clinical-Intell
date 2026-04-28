using System.Net.Http.Headers;
using NBomber.Contracts;
using NBomber.CSharp;
using UPACIP.LoadTests.Infrastructure;

namespace UPACIP.LoadTests.Scenarios;

/// <summary>
/// NBomber load scenario for the appointment booking flow (US_082 task_002, AC-1).
///
/// <para>
/// <b>Flow per virtual user:</b>
/// <list type="number">
///   <item><b>Slot lookup</b> â€” <c>GET /api/appointments/slots?providerId={id}&amp;date={tomorrow}</c></item>
///   <item><b>Book appointment</b> â€” <c>POST /api/appointments</c>. HTTP 409 is treated as
///     an expected concurrency conflict and is NOT counted as an error.</item>
///   <item><b>Confirm booking</b> â€” <c>GET /api/appointments/{id}</c></item>
/// </list>
/// </para>
///
/// <para>
/// <b>SLA:</b> Booking end-to-end P95 &lt; 2000 ms (AC-1).
/// </para>
///
/// <para>
/// <b>Concurrency:</b> 1000 virtual users, 120-second duration, 30-second ramp-up.
/// </para>
/// </summary>
public static class BookingLoadScenario
{
    private static readonly Random Rng = new();

    /// <summary>
    /// Creates and returns the NBomber <c>ScenarioProps</c> for the booking flow.
    /// </summary>
    /// <param name="config">Load test configuration.</param>
    /// <param name="concurrentUsers">Number of concurrent virtual users.</param>
    /// <param name="durationSeconds">Scenario run duration.</param>
    public static ScenarioProps Create(
        LoadTestConfiguration config,
        int concurrentUsers  = 1000,
        int durationSeconds  = 120)
    {
        var httpClient = BuildHttpClient(config, usePatientToken: true);

        var providerIds = Enumerable.Range(1, 50)
            .Select(i => new Guid(i, 0, 0, new byte[8]).ToString())
            .ToArray();

        return Scenario.Create("booking_flow", async ctx =>
        {
            var providerId = providerIds[Rng.Next(providerIds.Length)];
            var tomorrow   = DateTime.UtcNow.Date.AddDays(1).ToString("yyyy-MM-dd");

            // Step 1: Slot lookup â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            var slotUrl  = $"{config.BaseUrl}/api/appointments/slots?providerId={providerId}&date={tomorrow}";
            string? slotTime = null;

            using (var slotReq = new HttpRequestMessage(HttpMethod.Get, slotUrl))
            {
                using var slotResp = await httpClient.SendAsync(slotReq, CancellationToken.None);
                if (!slotResp.IsSuccessStatusCode)
                    return Response.Fail(message: $"SlotLookup:{(int)slotResp.StatusCode}");

                // Parse first available slot time from JSON (minimal string parse avoids
                // a full JSON deserialize to keep measurement overhead negligible).
                var json = await slotResp.Content.ReadAsStringAsync(CancellationToken.None);
                var match = System.Text.RegularExpressions.Regex.Match(
                    json, @"""slotTime""\s*:\s*""([^""]+)""");
                slotTime = match.Success ? match.Groups[1].Value : tomorrow + "T09:00:00Z";
            }

            // Step 2: Book appointment â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            string? appointmentId = null;

            using (var bookReq = new HttpRequestMessage(HttpMethod.Post,
                $"{config.BaseUrl}/api/appointments"))
            {
                var body = $$"""
                    {"providerId":"{{providerId}}","slotTime":"{{slotTime}}","reason":"Annual checkup (load test)"}
                    """;
                bookReq.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");

                using var bookResp = await httpClient.SendAsync(bookReq, CancellationToken.None);

                // HTTP 409 Conflict is expected under high concurrency â€” not an error.
                if (bookResp.StatusCode == System.Net.HttpStatusCode.Conflict)
                    return Response.Ok(); // booking conflict is acceptable

                if (!bookResp.IsSuccessStatusCode)
                    return Response.Fail(message: $"BookAppointment:{(int)bookResp.StatusCode}");

                var json = await bookResp.Content.ReadAsStringAsync(CancellationToken.None);
                var match = System.Text.RegularExpressions.Regex.Match(
                    json, @"""id""\s*:\s*""([^""]+)""");
                appointmentId = match.Success ? match.Groups[1].Value : null;
            }

            // Step 3: Confirm booking â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            if (!string.IsNullOrEmpty(appointmentId))
            {
                using var confirmReq = new HttpRequestMessage(HttpMethod.Get,
                    $"{config.BaseUrl}/api/appointments/{appointmentId}");
                using var confirmResp = await httpClient.SendAsync(confirmReq, CancellationToken.None);

                if (!confirmResp.IsSuccessStatusCode)
                    return Response.Fail(message: $"ConfirmBooking:{(int)confirmResp.StatusCode}");
            }

            return Response.Ok();
        })
        .WithLoadSimulations(
            Simulation.RampingInject(
                rate:     concurrentUsers / config.RampUpSeconds,
                interval: TimeSpan.FromSeconds(1),
                during:   TimeSpan.FromSeconds(config.RampUpSeconds)),
            Simulation.Inject(
                rate:     concurrentUsers / 10, // steady injection rate
                interval: TimeSpan.FromSeconds(1),
                during:   TimeSpan.FromSeconds(durationSeconds)));
    }

    // â”€â”€ Helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static HttpClient BuildHttpClient(LoadTestConfiguration config, bool usePatientToken)
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var token  = usePatientToken ? config.AuthTokenPatient : config.AuthTokenStaff;
        if (!string.IsNullOrEmpty(token))
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}


