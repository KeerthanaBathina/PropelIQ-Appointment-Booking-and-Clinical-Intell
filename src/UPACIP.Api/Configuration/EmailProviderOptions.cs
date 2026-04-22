// EmailProviderOptions lives in UPACIP.Service.Notifications following the same
// convention as AiGatewaySettings and ClinicSettings (settings classes belong in
// the Service project so they are accessible without a circular reference).
// Program.cs binds the section there via:
//   builder.Services.Configure<EmailProviderOptions>(
//       builder.Configuration.GetSection(EmailProviderOptions.SectionName));

namespace UPACIP.Api.Configuration;
