using NodaTime;
using Salon.Core.Catalog;
using Salon.Core.Import;
using Salon.Services.Clients;

namespace Salon.Services.Appointments.Import;

/// <summary>A client of the salon a row can be matched to: in use, not merged, not forgotten.</summary>
internal sealed record ImportClient(Guid Id, string FirstName, string LastName, string? Phone, string? Email);

/// <summary>A service of the salon, in use or not: one not in use is named as such rather than unknown.</summary>
internal sealed record ImportService(Guid Id, string Name, bool InUse);

/// <summary>A person of the salon, bookable or not: one who left is named as such rather than unknown.</summary>
internal sealed record ImportStaff(Guid Id, string Name, bool Bookable);

/// <summary>What a person charges and takes for a service; <see cref="Offers"/> false when they do not do it.</summary>
internal sealed record ImportOffering(bool Offers, int Minutes, decimal Price);

/// <summary>A visit in the diary that holds a person's time (booked, confirmed, arrived).</summary>
internal sealed record DiaryVisit(Guid Id, Guid StaffId, Guid ClientId, Instant Start, Instant End);

/// <summary>A visit brought over by an earlier file, in any status, by client, service and day.</summary>
internal sealed record ImportedVisit(Guid Id, Guid ClientId, Guid ServiceId, LocalDate Day, bool Active);

/// <summary>A time kept for a waiting client (ADR 0065).</summary>
internal sealed record HeldSlot(Guid StaffId, Instant Start, Instant End);

/// <summary>Everything a row is classified against, read through the tenant filter.</summary>
/// <param name="Now">The moment the file is read: earlier today is past.</param>
/// <param name="Offerings">By service and person, for the services the file names.</param>
internal sealed record AppointmentImportContext(
    DateTimeZone Zone,
    Instant Now,
    string Currency,
    IReadOnlyList<ImportClient> Clients,
    IReadOnlyList<ImportService> Services,
    IReadOnlyList<ImportStaff> Staff,
    IReadOnlyDictionary<(Guid ServiceId, Guid StaffId), ImportOffering> Offerings,
    IReadOnlyList<DiaryVisit> Diary,
    IReadOnlyList<ImportedVisit> Imported,
    IReadOnlyList<HeldSlot> Held);

/// <summary>
/// Pure classification of the rows of a diary file against the salon (ADR 0067). Every rule fails closed: a phone
/// or email that matches nobody is not tried as a name, a contact on a card of another first name is refused, a
/// person who left is never found by first name in a colleague, a status word the import does not know is not
/// booked. A visit brought over by an earlier file is never brought over again, whatever became of it here.
/// </summary>
internal static class AppointmentImportClassifier
{
    public static IReadOnlyList<ClassifiedAppointmentRow> Classify(IReadOnlyList<ImportedAppointmentRow> rows, AppointmentImportContext context)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(context);

        var clients = new ClientIndex(context.Clients, context.Currency);
        var services = context.Services.GroupBy(s => ImportNames.Key(s.Name)).ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.InUse).First(), StringComparer.Ordinal);
        var bookable = context.Staff.Where(s => s.Bookable).ToList();
        var diary = context.Diary.ToLookup(v => v.StaffId);
        var held = context.Held.ToLookup(h => h.StaffId);
        var imported = context.Imported.ToLookup(v => (v.ClientId, v.ServiceId, v.Day));
        var taken = new List<(Guid StaffId, Instant Start, Instant End)>();

        var result = new List<ClassifiedAppointmentRow>(rows.Count);
        foreach (var row in rows)
        {
            var problems = new List<AppointmentImportProblem>(row.Problems);
            var match = new AppointmentImportMatch();

            // The moment, in the salon's zone: a time the clocks skip or pass twice is not guessed.
            Instant? start = null;
            if (row.Start is { } localStart)
            {
                start = InZone(localStart, context.Zone, problems);
            }

            match = MatchClient(row, clients, match, problems);
            match = MatchService(row, services, match, problems);
            match = MatchStaff(row, context.Staff, bookable, match, problems);

            var status = AppointmentImportStatuses.Read(row.Status);
            var day = row.Start?.Date;

            // Cancelled in the old system: nothing is written. If a file brought it over before and it is still
            // booked here, the owner is told; otherwise there is nothing to do.
            if (status == OldStatus.Gone)
            {
                var since = match.ClientId is { } c && match.ServiceId is { } s && day is { } d
                    ? imported[(c, s, d)].FirstOrDefault(v => v.Active)
                    : null;
                result.Add(new ClassifiedAppointmentRow(
                    row,
                    since is null ? AppointmentImportStatus.Cancelled : AppointmentImportStatus.CancelledSince,
                    [],
                    match with { Start = start, ExistingAppointmentId = since?.Id }));
                continue;
            }

            if (start is { } begins && begins < context.Now)
            {
                result.Add(new ClassifiedAppointmentRow(row, AppointmentImportStatus.Past, [], match with { Start = start }));
                continue;
            }

            if (status == OldStatus.Unknown)
            {
                problems.Add(AppointmentImportProblem.UnknownStatus);
            }

            // The length: the file's end, or its minutes, or what the person takes for the service. Both an end and
            // minutes that disagree by more than a minute say the row is wrong somewhere.
            int? minutes = null;
            if (row.End is { } localEnd && start is { } from)
            {
                if (InZone(localEnd, context.Zone, problems) is { } until)
                {
                    var length = (until - from).TotalMinutes;
                    if (length <= 0 || length > AppointmentImportLimits.MaxMinutes || length != Math.Floor(length))
                    {
                        AddOnce(problems, AppointmentImportProblem.InvalidLength);
                    }
                    else
                    {
                        minutes = (int)length;
                        if (row.Minutes is { } given && Math.Abs(given - minutes.Value) > 1)
                        {
                            problems.Add(AppointmentImportProblem.LengthsDisagree);
                        }
                    }
                }
            }
            else if (row.Minutes is { } given)
            {
                if (given is <= 0 or > AppointmentImportLimits.MaxMinutes)
                {
                    AddOnce(problems, AppointmentImportProblem.InvalidLength);
                }
                else
                {
                    minutes = given;
                }
            }

            ImportOffering? offering = null;
            if (match.ServiceId is { } serviceId && match.StaffId is { } staffId)
            {
                if (!context.Offerings.TryGetValue((serviceId, staffId), out offering) || !offering.Offers)
                {
                    problems.Add(AppointmentImportProblem.StaffDoesNotOffer);
                }
            }

            minutes ??= offering?.Offers == true ? offering.Minutes : null;
            if (row.Price is { } price && !(ServiceRules.IsPriceInRange(price) && ServiceRules.HasAtMostTwoDecimals(price)))
            {
                AddOnce(problems, AppointmentImportProblem.InvalidPrice);
            }

            var end = start is { } s0 && minutes is { } m0 ? s0 + Duration.FromMinutes(m0) : (Instant?)null;
            match = match with
            {
                Start = start,
                End = end,
                Minutes = minutes,
                Price = row.Price ?? (offering?.Offers == true ? offering.Price : null),
            };

            if (problems.Count > 0)
            {
                result.Add(new ClassifiedAppointmentRow(row, AppointmentImportStatus.Invalid, problems, match));
                continue;
            }

            // Everything is known from here on.
            var client = match.ClientId!.Value;
            var service = match.ServiceId!.Value;
            var person = match.StaffId!.Value;
            var visitStart = start!.Value;
            var visitEnd = end!.Value;

            // Brought over by an earlier file (whatever became of it here), or the very visit already in the diary.
            var before = imported[(client, service, day!.Value)].FirstOrDefault();
            var same = diary[person].FirstOrDefault(v => v.ClientId == client && v.Start == visitStart);
            if (before is not null || same is not null)
            {
                result.Add(new ClassifiedAppointmentRow(row, AppointmentImportStatus.AlreadyThere, [], match with { ExistingAppointmentId = before?.Id ?? same!.Id }));
                continue;
            }

            // Half-open ranges, as the database's own rule: a visit may start when the last one ends.
            if (diary[person].Any(v => v.Start < visitEnd && visitStart < v.End))
            {
                problems.Add(AppointmentImportProblem.ClashesWithDiary);
            }

            if (taken.Any(t => t.StaffId == person && t.Start < visitEnd && visitStart < t.End))
            {
                problems.Add(AppointmentImportProblem.ClashesInFile);
            }

            if (held[person].Any(h => h.Start < visitEnd && visitStart < h.End))
            {
                problems.Add(AppointmentImportProblem.ClashesWithHeldTime);
            }

            if (problems.Count > 0)
            {
                result.Add(new ClassifiedAppointmentRow(row, AppointmentImportStatus.Invalid, problems, match));
                continue;
            }

            taken.Add((person, visitStart, visitEnd));
            result.Add(new ClassifiedAppointmentRow(row, AppointmentImportStatus.New, [], match));
        }

        return result;
    }

    private static Instant? InZone(LocalDateTime local, DateTimeZone zone, List<AppointmentImportProblem> problems)
    {
        if (ImportDates.InZone(local, zone) is { } instant)
        {
            return instant;
        }

        AddOnce(problems, ImportDates.Mappings(local, zone) == 0 ? AppointmentImportProblem.TimeSkipped : AppointmentImportProblem.TimeTwice);
        return null;
    }

    private static AppointmentImportMatch MatchClient(ImportedAppointmentRow row, ClientIndex clients, AppointmentImportMatch match, List<AppointmentImportProblem> problems)
    {
        // A number that cannot be read is no number: the row is matched on what else it gives.
        var phone = PhoneNumber.TryNormalise(row.ClientPhone, clients.Currency, out var normalised) ? normalised : null;
        var email = string.IsNullOrWhiteSpace(row.ClientEmail) ? null : row.ClientEmail.Trim().ToUpperInvariant();
        if (phone is null && email is null && string.IsNullOrWhiteSpace(row.ClientName))
        {
            problems.Add(AppointmentImportProblem.MissingClient);
            return match;
        }

        if (phone is not null || email is not null)
        {
            var byPhone = phone is null ? null : clients.ByPhone.GetValueOrDefault(phone);
            var byEmail = email is null ? null : clients.ByEmail.GetValueOrDefault(email);

            // Fail closed: a contact that is on no card is not tried as a name.
            if ((phone is not null && byPhone is null) || (email is not null && byEmail is null))
            {
                problems.Add(AppointmentImportProblem.UnknownClient);
                return match;
            }

            if (byPhone is not null && byEmail is not null && byPhone.Id != byEmail.Id)
            {
                problems.Add(AppointmentImportProblem.ContactsDisagree);
                return match;
            }

            var card = (byPhone ?? byEmail)!;
            var found = match with
            {
                ClientId = card.Id,
                ClientName = $"{card.FirstName} {card.LastName}",
                ClientMatchedBy = byPhone is not null ? ClientMatch.Phone : ClientMatch.Email,
            };

            // A family shares a phone: the number is the mother's, the visit the daughter's.
            if (!ImportNames.SamePerson(row.ClientName, card.FirstName))
            {
                problems.Add(AppointmentImportProblem.ContactBelongsToOther);
                return found with { ClientId = null };
            }

            return found;
        }

        var named = clients.ByName(row.ClientName);
        if (named.Count == 0)
        {
            problems.Add(AppointmentImportProblem.UnknownClient);
            return match;
        }

        if (named.Count > 1)
        {
            problems.Add(AppointmentImportProblem.AmbiguousClient);
            return match;
        }

        return match with { ClientId = named[0].Id, ClientName = $"{named[0].FirstName} {named[0].LastName}", ClientMatchedBy = ClientMatch.Name };
    }

    private static AppointmentImportMatch MatchService(ImportedAppointmentRow row, Dictionary<string, ImportService> services, AppointmentImportMatch match, List<AppointmentImportProblem> problems)
    {
        if (string.IsNullOrWhiteSpace(row.Service))
        {
            problems.Add(AppointmentImportProblem.MissingService);
            return match;
        }

        if (!services.TryGetValue(ImportNames.Key(row.Service), out var service))
        {
            problems.Add(AppointmentImportProblem.UnknownService);
            return match;
        }

        if (!service.InUse)
        {
            problems.Add(AppointmentImportProblem.ServiceNotInUse);
        }

        return match with { ServiceId = service.InUse ? service.Id : null, ServiceName = service.Name };
    }

    private static AppointmentImportMatch MatchStaff(ImportedAppointmentRow row, IReadOnlyList<ImportStaff> staff, List<ImportStaff> bookable, AppointmentImportMatch match, List<AppointmentImportProblem> problems)
    {
        if (string.IsNullOrWhiteSpace(row.Staff))
        {
            // A salon of one: the visit is with that person.
            if (bookable.Count == 1)
            {
                return match with { StaffId = bookable[0].Id, StaffName = bookable[0].Name };
            }

            problems.Add(AppointmentImportProblem.MissingStaff);
            return match;
        }

        // The full name among everybody, leavers included, so a leaver is named as such and never found in a colleague.
        var key = ImportNames.Key(row.Staff);
        var exact = staff.Where(s => ImportNames.Key(s.Name) == key).ToList();
        var byFirstName = false;
        if (exact.Count == 0)
        {
            var first = ImportNames.Words(row.Staff)[0];
            exact = staff.Where(s => ImportNames.Words(s.Name) is { Count: > 0 } words && words[0] == first).ToList();
            byFirstName = true;
        }

        if (exact.Count == 0)
        {
            problems.Add(AppointmentImportProblem.UnknownStaff);
            return match;
        }

        if (exact.Count > 1)
        {
            problems.Add(AppointmentImportProblem.AmbiguousStaff);
            return match;
        }

        var person = exact[0];
        if (!person.Bookable)
        {
            problems.Add(AppointmentImportProblem.StaffNotBookable);
            return match with { StaffName = person.Name };
        }

        return match with { StaffId = person.Id, StaffName = person.Name, StaffByFirstName = byFirstName };
    }

    private static void AddOnce(List<AppointmentImportProblem> problems, AppointmentImportProblem problem)
    {
        if (!problems.Contains(problem))
        {
            problems.Add(problem);
        }
    }

    /// <summary>The salon's clients by their numbers, emails and names, built once per file.</summary>
    private sealed class ClientIndex
    {
        private readonly ILookup<string, ImportClient> _names;

        public ClientIndex(IReadOnlyList<ImportClient> clients, string currency)
        {
            Currency = currency;
            ByPhone = clients.Where(c => c.Phone is not null).GroupBy(c => c.Phone!).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            ByEmail = clients.Where(c => c.Email is not null).GroupBy(c => c.Email!.Trim().ToUpperInvariant()).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

            // Both orders, as old systems write "Smith Sarah" as often as "Sarah Smith".
            _names = clients
                .SelectMany(c => new[] { (Key: ImportNames.Key($"{c.FirstName} {c.LastName}"), Client: c), (Key: ImportNames.Key($"{c.LastName} {c.FirstName}"), Client: c) })
                .Distinct()
                .ToLookup(x => x.Key, x => x.Client, StringComparer.Ordinal);
        }

        public string Currency { get; }

        public Dictionary<string, ImportClient> ByPhone { get; }

        public Dictionary<string, ImportClient> ByEmail { get; }

        public IReadOnlyList<ImportClient> ByName(string? name) => [.. _names[ImportNames.Key(name)].Distinct()];
    }
}