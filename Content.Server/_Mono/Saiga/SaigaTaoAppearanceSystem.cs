using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Preferences;
using Robust.Shared.Enums;
using Robust.Shared.Maths;

namespace Content.Server._Mono.Saiga;

/// <summary>
///     Applies the fixed "Цзинъи Тао" character profile to entities tagged with
///     <see cref="SaigaTaoAppearanceComponent"/> when they spawn. Built in code from the
///     exported profile so it only references markings that exist in this fork.
/// </summary>
public sealed class SaigaTaoAppearanceSystem : EntitySystem
{
    [Dependency] private readonly SharedHumanoidAppearanceSystem _humanoid = default!;
    [Dependency] private readonly MetaDataSystem _metaData = default!;

    private const string CharacterName = "Цзинъи Тао";

    private const string FlavorText =
        "Молодого возраста девушка азиатской наружности. Спортивное тело и низкий рост. " +
        "На лице небольшое количество пудры, глаза украшены чёрными стрелками. " +
        "Прямой нос и мягкие черты лица с тонкими, слегка улыбающимися губами. " +
        "Волосы заплетены в крупные пучки и украшены красными бантами. " +
        "Глаза цвета потускневшего нефрита.";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SaigaTaoAppearanceComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(EntityUid uid, SaigaTaoAppearanceComponent comp, MapInitEvent args)
    {
        if (!HasComp<HumanoidAppearanceComponent>(uid))
            return;

        var appearance = new HumanoidCharacterAppearance(
            hairStyleId: "HumanHairDoublebun",
            hairColor: Color.FromHex("#113932FF"),
            facialHairStyleId: HairStyles.DefaultFacialHairStyle, // "FacialHairShaved"
            facialHairColor: Color.FromHex("#11073AFF"),
            eyeColor: Color.FromHex("#144631FF"),
            skinColor: Color.FromHex("#E8C0A4FF"),
            markings: new List<Marking>
            {
                // CatEarsCurled из исходного профиля отсутствует в этом форке — опущено.
                new("TattooCampbellLeftArm", new List<Color> { Color.FromHex("#8D2146FF") }),
                new("TattooHiveChest", new List<Color> { Color.FromHex("#942948FF") }),
            });

        var profile = HumanoidCharacterProfile.DefaultWithSpecies("Human")
            .WithName(CharacterName)
            .WithAge(29)
            .WithSex(Sex.Female)
            .WithGender(Gender.Female)
            .WithSpecies("Human")
            .WithFlavorText(FlavorText)
            .WithCharacterAppearance(appearance);

        _humanoid.LoadProfile(uid, profile);
        _metaData.SetEntityName(uid, CharacterName);
    }
}
