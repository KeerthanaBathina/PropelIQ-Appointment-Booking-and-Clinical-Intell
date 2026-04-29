namespace UPACIP.Contracts.Services;

/// <summary>
/// Marker interface that identifies all service-layer classes in UPACIP (AC-1, TR-009).
///
/// All concrete service classes in <c>UPACIP.Service</c> implement this interface so that
/// NetArchTest architecture rules can identify and validate them by type membership.
///
/// The interface carries no members — it is purely structural, enabling:
/// <list type="bullet">
///   <item>Architecture validation: all types implementing <see cref="IServiceBase"/> must
///         reside in the <c>UPACIP.Service</c> namespace.</item>
///   <item>Dependency inversion: the Presentation layer references only interfaces from
///         <c>UPACIP.Contracts</c>, never concrete Service implementations or DataAccess types.</item>
/// </list>
/// </summary>
public interface IServiceBase
{
}
