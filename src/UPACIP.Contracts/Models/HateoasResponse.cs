namespace UPACIP.Contracts.Models;

/// <summary>
/// HATEOAS-enriched response wrapper for single-resource endpoints (US_096, AC-2, TR-011).
///
/// Extends the standard API envelope with a <see cref="Links"/> collection so that clients
/// receive self-describing responses. Each response includes at minimum a <c>self</c> link
/// and optionally includes related resource links and permitted action links.
///
/// Usage in controllers:
/// <code>
/// var hateoas = _wrapper.WrapResource(patient, "GetPatientById", new { id = patient.Id },
///     relatedLinks: [("GetPatientAppointments", new { patientId = patient.Id }, "appointments", "GET")]);
/// return Ok(hateoas);
/// </code>
/// </summary>
/// <typeparam name="T">The payload type returned on success.</typeparam>
public sealed class HateoasResponse<T>
{
    /// <summary>Response payload. Non-null when <see cref="Success"/> is true.</summary>
    public T? Data { get; init; }

    /// <summary>True when the request was processed successfully.</summary>
    public bool Success { get; init; }

    /// <summary>Human-readable summary message for the response.</summary>
    public string? Message { get; init; }

    /// <summary>
    /// Hypermedia links describing the current resource and its navigable neighbours.
    /// Always contains at least a <c>self</c> link when <see cref="Success"/> is true.
    /// </summary>
    public List<HateoasLink> Links { get; init; } = [];
}
