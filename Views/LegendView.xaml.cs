using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Lore.Views;

// One entry in the legend. ColorHex is set only for aspects (their line colour).
public sealed record LegendItem(string Symbol, string Name, string Group, string Short, string Detail, string? ColorHex = null);

// A group of legend items, shown under a header in the list.
public sealed class LegendGroup : List<LegendItem>
{
    public string Key { get; init; } = "";
    public LegendGroup(string key, IEnumerable<LegendItem> items) : base(items) => Key = key;
}

// Full-page reference: a grouped list of every glyph/colour/term on the left, and a
// fuller explanation of the clicked item on the right. Raises CloseRequested when the
// Back button is pressed.
public sealed partial class LegendView : UserControl
{
    public event EventHandler? CloseRequested;

    public LegendView()
    {
        InitializeComponent();

        var groups = Items
            .GroupBy(i => i.Group)
            .Select(g => new LegendGroup(g.Key, g))
            .ToList();
        GroupedItems.Source = groups;

        if (Items.Count > 0)
        {
            ShowDetail(Items[0]);
            ItemsList.SelectedItem = Items[0];
        }
    }

    private void Back_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        => CloseRequested?.Invoke(this, EventArgs.Empty);

    private void ItemsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ItemsList.SelectedItem is LegendItem item)
            ShowDetail(item);
    }

    private void ShowDetail(LegendItem item)
    {
        DetailSymbol.Text = item.Symbol;
        DetailName.Text = item.Name;
        DetailGroup.Text = item.Group;
        DetailText.Text = item.Detail;

        if (item.ColorHex is { } hex)
        {
            DetailSwatch.Background = new SolidColorBrush(ParseHex(hex));
            DetailSwatch.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
        }
        else
        {
            DetailSwatch.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        }
    }

    private static Color ParseHex(string hex)
    {
        hex = hex.TrimStart('#');
        return Color.FromArgb(255,
            Convert.ToByte(hex.Substring(0, 2), 16),
            Convert.ToByte(hex.Substring(2, 2), 16),
            Convert.ToByte(hex.Substring(4, 2), 16));
    }

    // ── Reference content ─────────────────────────────────────────────────────
    private static readonly List<LegendItem> Items =
    [
        // Planets & points
        new("☉", "Sun", "Planets & points", "identity, vitality",
            "Your core identity, ego, conscious will, and vitality. The sign the Sun occupies is what most people mean by “your sign.” It shows where you shine and what you are here to express."),
        new("☽", "Moon", "Planets & points", "emotions, needs",
            "Your emotional nature, instincts, and what makes you feel safe. It governs moods, memory, habit, and the need to nurture and be nurtured. The Moon changes sign roughly every 2½ days, so an accurate birth time matters for its exact degree."),
        new("☿", "Mercury", "Planets & points", "mind, speech",
            "The mind: how you think, learn, reason, and communicate. Rules speech, writing, curiosity, and short journeys. Mercury is never far from the Sun."),
        new("♀", "Venus", "Planets & points", "love, values",
            "Love, attraction, pleasure, and values. How you relate, what you find beautiful, and what you want to draw toward you — in romance, money, and taste."),
        new("♂", "Mars", "Planets & points", "drive, action",
            "Drive, assertion, desire, and anger. How you pursue what you want, take initiative, and defend yourself. The engine of action and courage."),
        new("♃", "Jupiter", "Planets & points", "growth, luck",
            "Growth, expansion, faith, and fortune. Where you seek meaning, take risks, and find abundance and opportunity — sometimes to excess."),
        new("♄", "Saturn", "Planets & points", "discipline, limits",
            "Discipline, structure, limits, and time. The taskmaster: where you meet responsibility, fear, and the slow, hard-won mastery that comes from effort."),
        new("♅", "Uranus", "Planets & points", "change, freedom",
            "Change, rebellion, and awakening — sudden shifts, invention, and the urge for freedom. A slow-moving outer planet whose sign is shared by a whole generation."),
        new("♆", "Neptune", "Planets & points", "dreams, ideals",
            "Dreams, imagination, spirituality, and illusion. Dissolves boundaries — inspiring compassion and art, or fog and escapism. A generational planet."),
        new("♇", "Pluto", "Planets & points", "power, transformation",
            "Power, death and rebirth, and deep transformation. Where you confront the buried and compulsive and are utterly remade. A generational planet."),
        new("☊", "North Node", "Planets & points", "life direction",
            "Not a planet but a point where the Moon’s orbit crosses the Sun’s path. It points toward the growth, qualities, and direction this life is calling you to develop."),
        new("⚷", "Chiron", "Planets & points", "wound & healing",
            "The “wounded healer” — a comet marking a core wound and the hard-won capacity to heal it, in yourself and in others."),
        new("⚸", "Black Moon Lilith", "Planets & points", "the untamed self",
            "Not a body but the Moon’s mean apogee — the empty focus of its orbit. Associated with raw instinct, autonomy, and the untamed, unapologetic self."),
        new("℞", "Retrograde", "Planets & points", "turned inward",
            "A planet that appears to move backward from Earth’s vantage. Its energy is expressed more inwardly, reflectively, or with delay rather than outwardly. Marked with the ℞ symbol next to a planet."),

        // Zodiac signs
        new("♈", "Aries", "Zodiac signs", "Cardinal Fire · Mars",
            "Cardinal Fire, ruled by Mars. Bold, direct, and pioneering — the initiator who leaps first and asks later."),
        new("♉", "Taurus", "Zodiac signs", "Fixed Earth · Venus",
            "Fixed Earth, ruled by Venus. Steady, sensual, and patient — devoted to comfort, security, and the tangible."),
        new("♊", "Gemini", "Zodiac signs", "Mutable Air · Mercury",
            "Mutable Air, ruled by Mercury. Curious, quick, and versatile — a communicator who thrives on variety and ideas."),
        new("♋", "Cancer", "Zodiac signs", "Cardinal Water · Moon",
            "Cardinal Water, ruled by the Moon. Nurturing, protective, and deeply feeling — attached to home, family, and memory."),
        new("♌", "Leo", "Zodiac signs", "Fixed Fire · Sun",
            "Fixed Fire, ruled by the Sun. Warm, proud, and expressive — a natural performer who leads from the heart."),
        new("♍", "Virgo", "Zodiac signs", "Mutable Earth · Mercury",
            "Mutable Earth, ruled by Mercury. Precise, practical, and discerning — devoted to craft, service, and improvement."),
        new("♎", "Libra", "Zodiac signs", "Cardinal Air · Venus",
            "Cardinal Air, ruled by Venus. Fair-minded, relational, and refined — drawn to harmony, beauty, and partnership."),
        new("♏", "Scorpio", "Zodiac signs", "Fixed Water · Mars/Pluto",
            "Fixed Water, ruled by Mars (and Pluto). Intense, private, and penetrating — drawn to depth, power, and transformation."),
        new("♐", "Sagittarius", "Zodiac signs", "Mutable Fire · Jupiter",
            "Mutable Fire, ruled by Jupiter. Adventurous, optimistic, and truth-seeking — a wanderer of far horizons and big ideas."),
        new("♑", "Capricorn", "Zodiac signs", "Cardinal Earth · Saturn",
            "Cardinal Earth, ruled by Saturn. Disciplined, ambitious, and enduring — a builder who climbs steadily toward mastery."),
        new("♒", "Aquarius", "Zodiac signs", "Fixed Air · Saturn/Uranus",
            "Fixed Air, ruled by Saturn (and Uranus). Independent, inventive, and principled — a reformer devoted to ideas and the collective."),
        new("♓", "Pisces", "Zodiac signs", "Mutable Water · Jupiter/Neptune",
            "Mutable Water, ruled by Jupiter (and Neptune). Compassionate, imaginative, and boundless — attuned to dreams and the unseen."),

        // Aspects
        new("☌", "Conjunction", "Aspects (line colours)", "0° · blends",
            "Two planets within a few degrees of each other, drawn as a white line. Their energies fuse and amplify — powerful, for better or worse, depending on the planets.", "#DCDCDC"),
        new("⚹", "Sextile", "Aspects (line colours)", "60° · easy",
            "Planets 60° apart, drawn in blue. An easy, supportive link — opportunity and talent that flow when you actively engage them.", "#5AA0E6"),
        new("□", "Square", "Aspects (line colours)", "90° · tension",
            "Planets 90° apart, drawn in red. Friction and tension between the two — challenging, but a powerful engine for growth and achievement.", "#DC3C3C"),
        new("△", "Trine", "Aspects (line colours)", "120° · flowing",
            "Planets 120° apart, drawn in green. Natural, effortless harmony — gifts that come easily, sometimes so easily they’re taken for granted.", "#50C864"),
        new("☍", "Opposition", "Aspects (line colours)", "180° · balance",
            "Planets 180° apart, drawn in orange. A push-pull between opposite needs — seeking balance, often played out through other people.", "#DC8C28"),

        // Chart angles
        new("ASC", "Ascendant", "Chart angles", "rising sign · left",
            "The degree rising on the eastern horizon at your birth, at the left (9 o’clock) of the wheel. Your “rising sign” — the mask you wear, first impressions, and physical body. Highly sensitive to birth time."),
        new("MC", "Midheaven", "Chart angles", "career · top",
            "The Medium Coeli, at the top of the wheel. Career, reputation, public role, and life direction — how the world sees you at your most visible."),
        new("DSC", "Descendant", "Chart angles", "partnership · right",
            "Opposite the Ascendant, at the right of the wheel. Partnership, marriage, and “the other” — the qualities you seek in and project onto close relationships."),
        new("IC", "Imum Coeli", "Chart angles", "home · bottom",
            "The bottom of the wheel, opposite the Midheaven. Home, roots, family, and your private inner foundation. Gold lines mark all four angles."),

        // Houses
        new("1", "1st House", "Houses", "self",
            "Self, body, appearance, and how you meet the world. Begins at the Ascendant."),
        new("2", "2nd House", "Houses", "money & worth",
            "Money, possessions, resources, and self-worth — what you value and what you own."),
        new("3", "3rd House", "Houses", "mind & communication",
            "Communication, learning, siblings, and short journeys — the everyday mind."),
        new("4", "4th House", "Houses", "home & roots",
            "Home, family, roots, and private life. Sits at the base of the chart (the IC)."),
        new("5", "5th House", "Houses", "creativity & romance",
            "Creativity, romance, play, children, and self-expression — what delights you."),
        new("6", "6th House", "Houses", "work & health",
            "Work, routine, service, and health — the daily grind and the body’s upkeep."),
        new("7", "7th House", "Houses", "partnership",
            "Partnership, marriage, and open relationships — one-to-one bonds. Begins at the Descendant."),
        new("8", "8th House", "Houses", "intimacy & change",
            "Shared resources, intimacy, transformation, death, and rebirth — what is deep and merged."),
        new("9", "9th House", "Houses", "meaning & travel",
            "Travel, higher learning, philosophy, and belief — the search for meaning and far horizons."),
        new("10", "10th House", "Houses", "career & public role",
            "Career, reputation, and public standing. Sits at the top of the chart (the Midheaven)."),
        new("11", "11th House", "Houses", "friends & hopes",
            "Friends, groups, community, and hopes for the future — your wider circle and ideals."),
        new("12", "12th House", "Houses", "the hidden",
            "The unconscious, solitude, retreat, and what stays hidden — endings and things behind the scenes."),

        // Dignity score
        new("🟢", "Extraordinary", "Chart assessment", "strong dignity",
            "A high traditional dignity score: the seven classical planets (Sun–Saturn) are largely strong and well-placed by sign, house, and condition. Highlighted with a green wash in the browse list."),
        new("⚪", "Ordinary", "Chart assessment", "typical",
            "A middling dignity score — the common, balanced case that most charts fall into. No highlight in the list."),
        new("🔴", "Alarming", "Chart assessment", "afflicted",
            "A low dignity score: notable weakness or affliction among the classical planets. “Interesting,” not doom — many remarkable lives score here. Highlighted with a red wash in the list."),
    ];
}
