using NodaTime;
using Salon.Core.Cqrs;
using Salon.Core.Reports;
using Salon.Services.Reports;

namespace Salon.Web.Features.Reports.Overview;

/// <summary>
/// The salon's numbers for a period (ADR 0026): two dates, a one-tap period, or this month when neither is usable.
/// A query: no transaction, tenant required.
/// </summary>
public sealed record ReportQuery(ReportPreset? Preset, LocalDate? From, LocalDate? To) : IQuery<SalonReport>;

public sealed class ReportHandler(IReportService reports) : IRequestHandler<ReportQuery, SalonReport>
{
    public Task<SalonReport> HandleAsync(ReportQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return reports.GetAsync(request.Preset, request.From, request.To, cancellationToken);
    }
}