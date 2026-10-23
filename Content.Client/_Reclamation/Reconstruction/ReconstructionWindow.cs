using System.Linq;
using System.Numerics;
using Content.Client.Humanoid;
using Content.Shared._Reclamation.Reconstruction;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Preferences;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Prototypes;
using static Robust.Client.UserInterface.Controls.BoxContainer;

namespace Content.Client._Reclamation.Reconstruction;

/// <summary>
/// First asks the patient whether they agree, then shows a cut-down character editor
/// for the parts of their look the pod can change.
/// </summary>
public sealed class ReconstructionWindow : DefaultWindow
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly MarkingManager _markings = default!;

    public event Action<bool>? OnConsent;
    public event Action<ReconstructionSubmitMessage>? OnSubmit;

    private readonly BoxContainer _consentBox;
    private readonly BoxContainer _editorBox;
    private readonly LineEdit _nameEdit;
    private readonly OptionButton _sexButton;
    private readonly Slider _skinSlider;
    private readonly ColorSelectorSliders _skinColor;
    private readonly EyeColorPicker _eyePicker;
    private readonly SingleMarkingPicker _hairPicker;
    private readonly SingleMarkingPicker _facialHairPicker;
    private readonly Slider _heightSlider;
    private readonly Slider _widthSlider;
    private readonly MarkingPicker _markingPicker;

    private SpeciesPrototype? _species;
    private HumanoidCharacterAppearance _appearance = default!;
    private Sex _sex;

    public ReconstructionWindow()
    {
        IoCManager.InjectDependencies(this);

        Title = Loc.GetString("reconstruction-window-title");
        MinSize = new Vector2(420, 180);

        var yes = new Button { Text = Loc.GetString("reconstruction-window-yes") };
        var no = new Button { Text = Loc.GetString("reconstruction-window-no") };
        yes.OnPressed += _ => ShowEditor();
        no.OnPressed += _ => OnConsent?.Invoke(false);

        _consentBox = new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            SeparationOverride = 10,
            Children =
            {
                new Label { Text = Loc.GetString("reconstruction-window-consent") },
                new BoxContainer
                {
                    Orientation = LayoutOrientation.Horizontal,
                    HorizontalAlignment = HAlignment.Center,
                    SeparationOverride = 10,
                    Children = { yes, no },
                },
            },
        };

        _nameEdit = new LineEdit { HorizontalExpand = true };
        _sexButton = new OptionButton();
        _skinSlider = new Slider { MinValue = 0, MaxValue = 100, HorizontalExpand = true };
        _skinColor = new ColorSelectorSliders();
        _eyePicker = new EyeColorPicker();
        _hairPicker = new SingleMarkingPicker { Category = MarkingCategories.Hair };
        _facialHairPicker = new SingleMarkingPicker { Category = MarkingCategories.FacialHair };
        _heightSlider = new Slider { HorizontalExpand = true };
        _widthSlider = new Slider { HorizontalExpand = true };
        _markingPicker = new MarkingPicker { IgnoreCategories = "Hair,FacialHair", MinHeight = 350 };

        var confirm = new Button { Text = Loc.GetString("reconstruction-window-confirm") };
        confirm.OnPressed += _ => Submit();

        _editorBox = new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            Visible = false,
            VerticalExpand = true,
            Children =
            {
                new ScrollContainer
                {
                    VerticalExpand = true,
                    HScrollEnabled = false,
                    Children =
                    {
                        new BoxContainer
                        {
                            Orientation = LayoutOrientation.Vertical,
                            SeparationOverride = 6,
                            Children =
                            {
                                Row("reconstruction-window-name", _nameEdit),
                                Row("reconstruction-window-sex", _sexButton),
                                Header("reconstruction-window-skin"),
                                _skinSlider,
                                _skinColor,
                                Header("reconstruction-window-eyes"),
                                _eyePicker,
                                Header("reconstruction-window-hair"),
                                _hairPicker,
                                _facialHairPicker,
                                Row("reconstruction-window-height", _heightSlider),
                                Row("reconstruction-window-width", _widthSlider),
                                Header("reconstruction-window-markings"),
                                _markingPicker,
                            },
                        },
                    },
                },
                confirm,
            },
        };

        Contents.AddChild(new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            Children = { _consentBox, _editorBox },
        });

        WireEvents();
    }

    private static Label Header(string loc) => new() { Text = Loc.GetString(loc), StyleClasses = { "LabelHeading" } };

    private static BoxContainer Row(string loc, Control control) => new()
    {
        Orientation = LayoutOrientation.Horizontal,
        SeparationOverride = 6,
        Children = { new Label { Text = Loc.GetString(loc), MinWidth = 90 }, control },
    };

    private void ShowEditor()
    {
        OnConsent?.Invoke(true);
        _consentBox.Visible = false;
        _editorBox.Visible = true;
        MinSize = new Vector2(620, 720);
        SetSize = MinSize;
    }

    public void SetState(ReconstructionEuiState state)
    {
        if (!_proto.TryIndex<SpeciesPrototype>(state.Species, out _species))
            return;

        _appearance = state.Appearance;
        _sex = state.Sex;
        _nameEdit.Text = state.Name;

        _sexButton.Clear();
        foreach (var sex in _species.Sexes)
            _sexButton.AddItem(Loc.GetString($"humanoid-profile-editor-sex-{sex.ToString().ToLower()}-text"), (int) sex);
        _sexButton.SelectId((int) _sex);

        _heightSlider.MinValue = _species.MinHeight;
        _heightSlider.MaxValue = _species.MaxHeight;
        _heightSlider.SetValueWithoutEvent(state.Height);
        _widthSlider.MinValue = _species.MinWidth;
        _widthSlider.MaxValue = _species.MaxWidth;
        _widthSlider.SetValueWithoutEvent(state.Width);

        var humanToned = _species.SkinColoration == HumanoidSkinColor.HumanToned;
        _skinSlider.Visible = humanToned;
        _skinColor.Visible = !humanToned;
        if (humanToned)
            _skinSlider.SetValueWithoutEvent(SkinColor.HumanSkinToneFromColor(_appearance.SkinColor));
        else
            _skinColor.Color = _appearance.SkinColor;

        _eyePicker.SetData(_appearance.EyeColor);
        UpdateHairPickers();
        _markingPicker.SetData(_appearance.Markings, _species.ID, _sex, _appearance.SkinColor, _appearance.EyeColor);
        UpdateMarkingHair();
    }

    private void WireEvents()
    {
        _sexButton.OnItemSelected += args =>
        {
            _sexButton.SelectId(args.Id);
            _sex = (Sex) args.Id;
            _markingPicker.SetSex(_sex);
        };

        _skinSlider.OnValueChanged += _ => SetSkin(SkinColor.HumanSkinTone((int) _skinSlider.Value));
        _skinColor.OnColorChanged += color => SetSkin(ConvertSkin(color));

        _eyePicker.OnEyeColorPicked += color =>
        {
            _appearance = _appearance.WithEyeColor(color);
            _markingPicker.CurrentEyeColor = color;
        };

        _hairPicker.OnMarkingSelect += style => _appearance = _appearance.WithHairStyleName(style.id);
        _hairPicker.OnColorChanged += color =>
        {
            _appearance = _appearance.WithHairColor(color.marking.MarkingColors[0]);
            UpdateMarkingHair();
        };
        _hairPicker.OnSlotRemove += _ => SetHair(HairStyles.DefaultHairStyle);
        _hairPicker.OnSlotAdd += () => SetHair(FirstStyle(MarkingCategories.Hair));

        _facialHairPicker.OnMarkingSelect += style => _appearance = _appearance.WithFacialHairStyleName(style.id);
        _facialHairPicker.OnColorChanged += color =>
        {
            _appearance = _appearance.WithFacialHairColor(color.marking.MarkingColors[0]);
            UpdateMarkingHair();
        };
        _facialHairPicker.OnSlotRemove += _ => SetFacialHair(HairStyles.DefaultFacialHairStyle);
        _facialHairPicker.OnSlotAdd += () => SetFacialHair(FirstStyle(MarkingCategories.FacialHair));

        _markingPicker.OnMarkingAdded += SetMarkings;
        _markingPicker.OnMarkingRemoved += SetMarkings;
        _markingPicker.OnMarkingColorChange += SetMarkings;
        _markingPicker.OnMarkingRankChange += SetMarkings;
    }

    private void SetSkin(Color color)
    {
        _appearance = _appearance.WithSkinColor(color);
        _markingPicker.CurrentSkinColor = color;
    }

    /// <summary>Snap a picked color to what this species' skin type allows, same as the lobby editor.</summary>
    private Color ConvertSkin(Color color)
    {
        if (_species == null)
            return color;

        return _species.SkinColoration switch
        {
            HumanoidSkinColor.TintedHues => SkinColor.TintedHues(color),
            HumanoidSkinColor.TintedHuesSkin => SkinColor.TintedHuesSkin(color, _species.DefaultSkinTone),
            HumanoidSkinColor.VoxFeathers => SkinColor.ClosestVoxColor(color),
            HumanoidSkinColor.AnimalFur => SkinColor.ClosestAnimalFurColor(color),
            _ => color,
        };
    }

    private string FirstStyle(MarkingCategories category)
    {
        return _species == null
            ? string.Empty
            : _markings.MarkingsByCategoryAndSpecies(category, _species.ID).Keys.FirstOrDefault() ?? string.Empty;
    }

    private void SetHair(string style)
    {
        if (string.IsNullOrEmpty(style))
            return;

        _appearance = _appearance.WithHairStyleName(style);
        UpdateHairPickers();
        UpdateMarkingHair();
    }

    private void SetFacialHair(string style)
    {
        if (string.IsNullOrEmpty(style))
            return;

        _appearance = _appearance.WithFacialHairStyleName(style);
        UpdateHairPickers();
        UpdateMarkingHair();
    }

    private void SetMarkings(MarkingSet set)
    {
        _appearance = _appearance.WithMarkings(set.GetForwardEnumerator().ToList());
    }

    private void UpdateHairPickers()
    {
        if (_species == null)
            return;

        var hair = _appearance.HairStyleId == HairStyles.DefaultHairStyle
            ? new List<Marking>()
            : new List<Marking> { new(_appearance.HairStyleId, new List<Color> { _appearance.HairColor }) };
        var facialHair = _appearance.FacialHairStyleId == HairStyles.DefaultFacialHairStyle
            ? new List<Marking>()
            : new List<Marking> { new(_appearance.FacialHairStyleId, new List<Color> { _appearance.FacialHairColor }) };

        _hairPicker.UpdateData(hair, _species.ID, 1);
        _facialHairPicker.UpdateData(facialHair, _species.ID, 1);
    }

    /// <summary>Some markings take their color from the hair, so the marking picker needs to know it.</summary>
    private void UpdateMarkingHair()
    {
        _markingPicker.HairMarking = _appearance.HairStyleId == HairStyles.DefaultHairStyle
            ? null
            : new Marking(_appearance.HairStyleId, new List<Color> { _appearance.HairColor });
        _markingPicker.FacialHairMarking = _appearance.FacialHairStyleId == HairStyles.DefaultFacialHairStyle
            ? null
            : new Marking(_appearance.FacialHairStyleId, new List<Color> { _appearance.FacialHairColor });
    }

    private void Submit()
    {
        var name = _nameEdit.Text.Trim();
        if (name.Length > HumanoidCharacterProfile.MaxNameLength)
            name = name[..HumanoidCharacterProfile.MaxNameLength];

        OnSubmit?.Invoke(new ReconstructionSubmitMessage(name, _sex, _heightSlider.Value, _widthSlider.Value, _appearance));
    }
}
