using Fushi.Application.Abstractions.Messaging;
using Fushi.Application.Abstractions.Persistence.Repositories;
using Fushi.Core.Entities.Submissions;
using Fushi.Core.Results;

using FluentValidation;

namespace Fushi.Application.Features.Submissions;

/// <summary>
/// Finds the submissions whose code or title begins with what a user has typed,
/// for the autocomplete on every command that takes a code.
/// </summary>
/// <remarks>
/// Discord displays at most 25 choices and allows roughly three seconds to
/// answer an autocomplete interaction before it gives up and shows the user
/// nothing. Both numbers are why this is a prefix match with a hard cap rather
/// than a general search: a fuzzy or substring match over a guild's whole history
/// cannot be served from an index, and the query that would be needed is exactly
/// the one that misses the deadline.
/// <br/>
/// An empty prefix is a legitimate request rather than an error. A user who has
/// clicked into the box and typed nothing should still be offered the most recent
/// submissions, which is almost always what they came for.
/// </remarks>
/// <param name="GuildId">The guild to search.</param>
/// <param name="Prefix">
/// What the user has typed so far, or empty for the most recent submissions.
/// </param>
/// <param name="Limit">
/// The most choices to return. Clamped to <see cref="MAX_CHOICES"/>, because
/// Discord rejects a response carrying more.
/// </param>
/// <seealso cref="SubmissionChoiceModel"/>
public sealed record SearchSubmissions(
    ulong GuildId,
    string Prefix = "",
    int Limit = 25) : IQuery<IReadOnlyList<SubmissionChoiceModel>>
{
    /// <summary>
    /// The most choices Discord will display for one autocomplete interaction.
    /// </summary>
    public const int MAX_CHOICES = 25;

    /// <summary>
    /// The longest prefix accepted, past which no code and no title could still
    /// match.
    /// </summary>
    public const int MAX_PREFIX_LENGTH = Submission.MAX_TITLE_LENGTH;
}

/// <summary>
/// Checks the shape of a <see cref="SearchSubmissions"/> query.
/// </summary>
/// <remarks>
/// The limit is not checked, because the handler clamps it: an autocomplete that
/// answered with a validation failure would show the user an empty list and no
/// explanation, which is worse than quietly returning 25.
/// </remarks>
internal sealed class SearchSubmissionsValidator : AbstractValidator<SearchSubmissions>
{
    /// <summary>
    /// Initialises the rule set.
    /// </summary>
    public SearchSubmissionsValidator()
    {
        RuleFor(query => query.GuildId)
            .NotEqual(0uL)
            .WithMessage("A guild is required.");

        RuleFor(query => query.Prefix)
            .NotNull()
            .WithMessage("A prefix is required; use an empty string to match everything.")
            .MaximumLength(SearchSubmissions.MAX_PREFIX_LENGTH)
            .WithMessage("That is longer than anything it could match.");
    }
}

/// <summary>
/// Carries out <see cref="SearchSubmissions"/>.
/// </summary>
/// <param name="submissions">The submission store.</param>
internal sealed class SearchSubmissionsHandler(ISubmissionRepository submissions)
    : IQueryHandler<SearchSubmissions, IReadOnlyList<SubmissionChoiceModel>>
{
    /// <summary>
    /// The longest label Discord accepts for a single choice.
    /// </summary>
    private const int MAX_LABEL_LENGTH = 100;

    private const string SEPARATOR = " — ";

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<SubmissionChoiceModel>>> HandleAsync(
        SearchSubmissions request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        int limit = Math.Clamp(request.Limit, 1, SearchSubmissions.MAX_CHOICES);

        IReadOnlyList<Submission> matches = await submissions.SearchAsync(
            request.GuildId,
            request.Prefix,
            limit,
            cancellationToken);

        var choices = new SubmissionChoiceModel[matches.Count];
        for (int index = 0; index < matches.Count; index++)
        {
            Submission submission = matches[index];
            string code = submission.Code.ToString();

            choices[index] = new SubmissionChoiceModel(Label(code, submission.Title), code);
        }

        return Result<IReadOnlyList<SubmissionChoiceModel>>.Success(choices);
    }

    // The code leads, because it is what gets submitted and what the user will
    // see echoed back. Discord truncates an over-long label itself, but it does
    // so without an ellipsis, which reads as though the title simply stopped.
    private static string Label(string code, string title)
    {
        int room = MAX_LABEL_LENGTH - code.Length - SEPARATOR.Length;
        if (title.Length <= room)
        {
            return code + SEPARATOR + title;
        }

        return room <= 1
            ? code
            : string.Concat(code, SEPARATOR, title.AsSpan(0, room - 1), "…");
    }
}

/// <summary>
/// One autocomplete choice: what the user reads, and what gets sent when they
/// pick it.
/// </summary>
/// <remarks>
/// The two are separate because they serve different readers. The label carries
/// the title so a moderator can recognise the submission they mean; the value
/// carries only the code, because that is what the command being completed
/// expects and anything else would fail to resolve.
/// </remarks>
/// <param name="Label">What is shown in the dropdown.</param>
/// <param name="Value">The code submitted when the choice is picked.</param>
public sealed record SubmissionChoiceModel(string Label, string Value);
