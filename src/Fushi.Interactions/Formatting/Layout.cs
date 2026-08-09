using System.Globalization;
using System.Text;

using Discord;

namespace Fushi.Interactions.Formatting;

/// <summary>
/// The building blocks every view in this project is assembled from, using
/// Discord's second-generation components.
/// </summary>
/// <remarks>
/// A components-v2 message carries no content and no embeds — the two are
/// mutually exclusive, so adopting v2 means every view returns a
/// <see cref="MessageComponent"/> and every send sets
/// <see cref="MessageFlags.ComponentsV2"/>. What that buys is worth the
/// conversion: a container gives the accent stripe an embed had, but its contents
/// are ordinary components, so a row of a list can carry its own button and a
/// heading can be separated from a body by a real rule rather than by blank lines.
/// <br/>
/// Two ceilings shape everything here. A message may hold forty components in
/// total, counting a container and each of its children separately, and four
/// thousand characters across every text block. A page of ten rows, each a section
/// with a button, is already thirty of those forty — which is why lists put their
/// navigation in one row and never nest a separator between rows.
/// </remarks>
internal static class Layout
{
    /// <summary>
    /// The most characters one body of text may contribute.
    /// </summary>
    /// <remarks>
    /// Well under the four thousand a whole message may carry, because a
    /// submission's text shares the message with its tally, its status, and its
    /// buttons. Truncating here rather than letting Discord refuse the message
    /// means an over-long application is still readable rather than absent.
    /// </remarks>
    public const int MAX_BODY_LENGTH = 1600;

    /// <summary>
    /// Assembles a finished message from a single accented container.
    /// </summary>
    /// <param name="accent">The stripe colour down the container's edge.</param>
    /// <param name="parts">What goes inside, in order.</param>
    /// <returns>The message.</returns>
    public static MessageComponent Panel(Color accent, params IMessageComponentBuilder[] parts)
    {
        ContainerBuilder container = new ContainerBuilder().WithAccentColor(accent);

        foreach (IMessageComponentBuilder part in parts)
        {
            container.AddComponent(part);
        }

        return new ComponentBuilderV2().AddComponent(container).Build();
    }

    /// <summary>
    /// Renders a heading.
    /// </summary>
    /// <remarks>
    /// A markdown heading rather than a bold line, because v2 has no title field
    /// and Discord renders <c>##</c> at a genuinely larger size — which is what
    /// makes a container scannable at the speed an embed's title was.
    /// </remarks>
    /// <param name="text">The heading.</param>
    /// <returns>The component.</returns>
    public static TextDisplayBuilder Heading(string text) => new($"## {text}");

    /// <summary>
    /// Renders a smaller heading, for a block inside a panel.
    /// </summary>
    /// <param name="text">The heading.</param>
    /// <returns>The component.</returns>
    public static TextDisplayBuilder Subheading(string text) => new($"### {text}");

    /// <summary>
    /// Renders a block of text as it was given.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The component.</returns>
    public static TextDisplayBuilder Text(string text) => new(text);

    /// <summary>
    /// Renders a note in Discord's small, dimmed style.
    /// </summary>
    /// <remarks>
    /// This is what replaces an embed's footer. Keeping it visually quieter than
    /// the body matters: a page counter and a short code are reference material
    /// somebody looks for deliberately, not something that should compete with
    /// the thing they came to read.
    /// </remarks>
    /// <param name="text">The note.</param>
    /// <returns>The component.</returns>
    public static TextDisplayBuilder Note(string text) => new($"-# {text}");

    /// <summary>
    /// Renders a horizontal rule.
    /// </summary>
    /// <param name="large">Whether to leave more space around it.</param>
    /// <returns>The component.</returns>
    public static SeparatorBuilder Rule(bool large = false) => new(
        true,
        large ? SeparatorSpacingSize.Large : SeparatorSpacingSize.Small,
        null);

    /// <summary>
    /// Renders blank space with no rule drawn through it.
    /// </summary>
    /// <param name="large">Whether to leave more space.</param>
    /// <returns>The component.</returns>
    public static SeparatorBuilder Gap(bool large = false) => new(
        false,
        large ? SeparatorSpacingSize.Large : SeparatorSpacingSize.Small,
        null);

    /// <summary>
    /// Renders a row of labelled values as a single block.
    /// </summary>
    /// <remarks>
    /// The replacement for an embed's inline fields, which v2 has no equivalent
    /// of. Rendered as one text block with bold labels rather than as separate
    /// components, because ten fields would otherwise be ten of the forty
    /// components a message is allowed.
    /// </remarks>
    /// <param name="fields">The label and value of each entry.</param>
    /// <returns>The component.</returns>
    public static TextDisplayBuilder Fields(params ReadOnlySpan<(string Label, string Value)> fields)
    {
        StringBuilder text = new(fields.Length * 48);

        foreach ((string label, string value) in fields)
        {
            if (text.Length > 0)
            {
                text.Append('\n');
            }

            text.Append(CultureInfo.InvariantCulture, $"**{label}** · {value}");
        }

        return new TextDisplayBuilder(text.ToString());
    }

    /// <summary>
    /// Renders one row of a list with a button of its own beside it.
    /// </summary>
    /// <remarks>
    /// The single largest gain over an embed. A list used to be a paragraph of
    /// codes that had to be copied into another command; each row can now act on
    /// itself, which removes the step where somebody mistypes a code.
    /// </remarks>
    /// <param name="text">The row's text.</param>
    /// <param name="label">The button's label.</param>
    /// <param name="customId">The button's identifier.</param>
    /// <param name="style">How the button should look.</param>
    /// <returns>The component.</returns>
    public static SectionBuilder Row(
        string text,
        string label,
        string customId,
        ButtonStyle style = ButtonStyle.Secondary) =>
        new SectionBuilder()
            .AddComponent(new TextDisplayBuilder(text))
            .WithAccessory(new ButtonBuilder(label, customId, style));

    /// <summary>
    /// Builds a row of buttons.
    /// </summary>
    /// <param name="buttons">The buttons, in order.</param>
    /// <returns>The component.</returns>
    public static ActionRowBuilder Actions(params ButtonBuilder[] buttons)
    {
        ActionRowBuilder row = new();

        foreach (ButtonBuilder button in buttons)
        {
            row.WithButton(button);
        }

        return row;
    }

    /// <summary>
    /// Shortens text that would otherwise crowd out the rest of a panel.
    /// </summary>
    /// <remarks>
    /// Cuts on the last whitespace before the limit where there is one, so the
    /// result ends on a word rather than mid-syllable.
    /// </remarks>
    /// <param name="text">The text to shorten.</param>
    /// <param name="limit">The most characters to keep.</param>
    /// <returns>The text, shortened if it had to be.</returns>
    public static string Clamp(string text, int limit = MAX_BODY_LENGTH)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= limit)
        {
            return text;
        }

        ReadOnlySpan<char> kept = text.AsSpan(0, limit - 1);
        int lastSpace = kept.LastIndexOfAny(" \n\r\t");

        if (lastSpace > limit / 2)
        {
            kept = kept[..lastSpace];
        }

        return string.Concat(kept.TrimEnd(), "…");
    }
}
