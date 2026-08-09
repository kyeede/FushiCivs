using Discord;
using Discord.Interactions;

using Fushi.Application.Abstractions.Messaging;
using Fushi.Application.Features.Submissions;
using Fushi.Core.Results;

using Microsoft.Extensions.DependencyInjection;

namespace Fushi.Interactions.Autocomplete;

/// <summary>
/// Offers submissions for any option that takes a submission's short code.
/// </summary>
/// <remarks>
/// Short codes are six characters of Crockford Base32 and are not meant to be
/// memorised. This handler is what makes that acceptable: somebody types two
/// characters of a code, or a word from the title, and picks the right entry from
/// a list. Typing the whole code still works, which is what matters when one
/// arrives in a screenshot or over voice.
/// <br/>
/// The search runs against code prefix and title substring together, scoped to
/// the invoking guild, so a code from another server can never be offered.
/// <br/>
/// Discord sends a request on roughly every keystroke and gives a short deadline
/// to answer. A failure is therefore answered with an empty list rather than an
/// error: an autocomplete that reports a problem cannot be read, since the option
/// is still mid-edit, and an empty list at least leaves typing the code in full
/// available.
/// </remarks>
public sealed class SubmissionCodeAutocompleteHandler : AutocompleteHandler
{
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

        Result<IReadOnlyList<SubmissionChoiceModel>> result = await dispatcher.SendAsync(
            new SearchSubmissions(context.Guild.Id, prefix, SearchSubmissions.MAX_CHOICES));

        return result.IsFailure
            ? AutocompletionResult.FromSuccess()
            : AutocompletionResult.FromSuccess(
                result.Value.Select(choice => new AutocompleteResult(choice.Label, choice.Value)));
    }
}
