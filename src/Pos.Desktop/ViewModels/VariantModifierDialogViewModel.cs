using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pos.Core.Domain;

namespace Pos.Desktop.ViewModels;

/// <summary>One selectable modifier, inside a <see cref="ModifierGroupSelectionViewModel"/>.
/// Selection is driven through <see cref="ToggleCommand"/>, not a two-way-bound checkbox,
/// so min/max-select enforcement (in the owning group) always runs before the UI updates.</summary>
public sealed partial class ModifierOptionSelectionViewModel(ModifierOption option, ModifierGroupSelectionViewModel group) : ViewModelBase
{
    public ModifierOption Option { get; } = option;
    public string Name => Option.Name;
    public decimal PriceAdjustment => Option.PriceAdjustment;

    [ObservableProperty] private bool _isSelected;

    [RelayCommand]
    private void Toggle() => group.Toggle(this);

    public void SetSelectedInternal(bool value) => IsSelected = value;
}

/// <summary>One modifier group's selection state. <see cref="MaxSelect"/> of 1 behaves
/// like a radio group (picking a new option clears the old one); any higher max blocks
/// a new pick once <see cref="MaxSelect"/> is already reached — both rules ported
/// directly from the web cashier's <c>VariantModifierModal.jsx</c> <c>toggleModifier</c>.</summary>
public sealed partial class ModifierGroupSelectionViewModel : ViewModelBase
{
    private readonly Action _onChanged;

    public ModifierGroupSelectionViewModel(ModifierGroupOptions source, Action onChanged)
    {
        GroupId = source.GroupId;
        Name = source.Name;
        MinSelect = source.MinSelect;
        MaxSelect = source.MaxSelect;
        _onChanged = onChanged;
        Options = new ObservableCollection<ModifierOptionSelectionViewModel>(source.Options.Select(o => new ModifierOptionSelectionViewModel(o, this)));
    }

    public string GroupId { get; }
    public string Name { get; }
    public int MinSelect { get; }
    public int MaxSelect { get; }
    public ObservableCollection<ModifierOptionSelectionViewModel> Options { get; }

    public string Requirement => MinSelect > 0 ? $"Choose {MinSelect}{(MaxSelect > MinSelect ? $"-{MaxSelect}" : "")}" : $"Choose up to {MaxSelect}";

    public int SelectedCount => Options.Count(o => o.IsSelected);
    public bool IsSatisfied => SelectedCount >= MinSelect;
    public IReadOnlyList<ModifierOption> SelectedOptions => [.. Options.Where(o => o.IsSelected).Select(o => o.Option)];

    public void Toggle(ModifierOptionSelectionViewModel option)
    {
        if (option.IsSelected)
        {
            option.SetSelectedInternal(false);
        }
        else if (MaxSelect == 1)
        {
            foreach (var other in Options) other.SetSelectedInternal(false);
            option.SetSelectedInternal(true);
        }
        else if (SelectedCount < MaxSelect)
        {
            option.SetSelectedInternal(true);
        }
        // else: at max already — blocked, matching the web modal's behaviour.

        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(IsSatisfied));
        _onChanged();
    }
}

/// <summary>
/// Variant + modifier picker, ported from <c>VariantModifierModal.jsx</c>. Shown only
/// when <see cref="CatalogProduct.NeedsPicker"/> is true; a single-variant, no-modifier
/// product goes straight into the cart without this dialog.
/// </summary>
public sealed partial class VariantModifierDialogViewModel : ViewModelBase
{
    public VariantModifierDialogViewModel(CatalogProduct product, IReadOnlyList<ModifierGroupOptions> modifierGroups)
    {
        ProductName = product.Name;
        Variants = [.. product.Variants];
        SelectedVariant = Variants.Count == 1 ? Variants[0] : null;
        Groups = [.. modifierGroups.Select(g => new ModifierGroupSelectionViewModel(g, OnSelectionChanged))];
    }

    public string ProductName { get; }
    public IReadOnlyList<CatalogVariant> Variants { get; }
    public ObservableCollection<ModifierGroupSelectionViewModel> Groups { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirm))]
    [NotifyPropertyChangedFor(nameof(PreviewUnitPrice))]
    private CatalogVariant? _selectedVariant;

    [ObservableProperty] private int _quantity = 1;

    public bool CanConfirm => SelectedVariant is not null && Quantity > 0 && Groups.All(g => g.IsSatisfied);
    public decimal PreviewUnitPrice => (SelectedVariant?.Price ?? 0) + Groups.SelectMany(g => g.SelectedOptions).Sum(o => o.PriceAdjustment);

    [RelayCommand]
    private void SelectVariant(CatalogVariant variant) => SelectedVariant = variant;

    /// <summary>Set by the view's code-behind once the user confirms; null means cancelled.</summary>
    public CartLine? Result { get; private set; }

    [RelayCommand]
    private void Confirm()
    {
        if (!CanConfirm || SelectedVariant is null) return;
        Result = new CartLine
        {
            VariantId = SelectedVariant.VariantId,
            ProductName = ProductName,
            Sku = SelectedVariant.Sku,
            BaseUnitPrice = SelectedVariant.Price,
            Modifiers = [.. Groups.SelectMany(g => g.SelectedOptions).Select(o => new CartModifier(o.Id, o.Name, o.PriceAdjustment))],
            Quantity = Quantity,
            TaxRate = SelectedVariant.TaxRate,
        };
        RequestClose?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel() => RequestClose?.Invoke(false);

    /// <summary>Wired by the view's code-behind to actually close the Window.</summary>
    public Action<bool>? RequestClose { get; set; }

    private void OnSelectionChanged()
    {
        OnPropertyChanged(nameof(CanConfirm));
        OnPropertyChanged(nameof(PreviewUnitPrice));
    }
}
