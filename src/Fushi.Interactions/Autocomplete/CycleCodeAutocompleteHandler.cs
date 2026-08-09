using System.Globalization;

using Discord;
using Discord.Interactions;

using Fushi.Application.Abstractions.Messaging;
using Fushi.Application.Features.Cycles;
using Fushi.Core.Results;
using Fushi.Core.Utilities.Paging;
using Fushi.Interactions.Formatting;

using Microsoft.Extensions.DependencyInjection;

namespace Fushi.Interactions.Autocomplete;

/// <summary>
/// Offers recent cycles for any option that takes a cycle's short code.
/// </summary>
/// <remarks>
/// Cycles are addressed far less often than submissions, and there are few enough
/// of them that the newest page is almost always the one wanted. So this asks for
/// a single page and filters it here rather than pushing a prefix search into the
/// query layer, which would be a repository method existing solely for a
/// keystroke.
/// <br/>
/// Each choice shows the code, the date, and the status together, because "which
/// cycle" is a question people answer by date rather than by code.
/// </remarks>
public sealed class CycleCodeAutocompleteHandler : AutocompleteHandler
{
    private const int PAGE_SIZE = 25;

    /// <inheritdoc/>
    public override async Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context,
        IAutocompleteInteraction autocompleteInteraction,
        IParameterInfo parameter,
        IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(autocompleteInteraction);
        ArgumentNullException.ThrowIfNull(services);

        if (context.Guild is null)
        {
            return AutocompletionResult.FromSuccess();
        }

        IDispatcher dispatcher = services.GetRequiredService<IDispatcher>();
        string prefix = autocompleteInteraction.Data.Current.Value as string ?? string.Empty;

        Result<Page<CycleSummaryModel>> result = await dispatcher.SendAsync(
            new ListCycles(context.Guild.Id, PageRequest.Clamp(1, PAGE_SIZE)));

        if (result.IsFailure)
        {
            return AutocompletionResult.FromSuccess();
        }

        IEnumerable<AutocompleteResult> choices = result.Value
            .Where(cycle => Matches(cycle, prefix))
            .Select(cycle => new AutocompleteResult(Label(cycle), cycle.Code.ToString()));

        return AutocompletionResult.FromSuccess(choices);
    }

    private static bool Matches(CycleSummaryModel cycle, string prefix) =>
        prefix.Length == 0
        || cycle.Code.ToString().StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
        || cycle.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            .Contains(prefix, StringComparison.OrdinalIgnoreCase);

    private static string Label(CycleSummaryModel cycle) => string.Create(
        CultureInfo.InvariantCulture,
        $"{cycle.Code} — {cycle.Date:yyyy-MM-dd} — {Display.Of(cycle.Status)}");
}
