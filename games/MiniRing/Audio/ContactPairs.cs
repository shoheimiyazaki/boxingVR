using MiniRing.Voxels;

namespace MiniRing.Audio;

/// <summary>
/// ぶつかった 2 つの面の材質（<see cref="BoxerAnatomy"/> の番号）から、当たった音の種類（<see cref="ContactPair"/>）を選ぶ。
/// 材質を、音の鳴り方の違う 4 つ（鉄・木・革（グローブの革と詰め物・ブーツ）・肉（皮膚・肉・骨・臓器・髪・服））に分けて、組で選ぶ。
/// 順番は問わない。どちらかが分からなければ null（鳴らさない）。
/// </summary>
public static class ContactPairs
{
    private enum Stuff
    {
        Metal,
        Wood,
        Leather,
        Flesh,
    }

    /// <summary>面の材質 <paramref name="a"/> と <paramref name="b"/> がぶつかった音の種類。</summary>
    public static ContactPair? Of(byte a, byte b)
    {
        if (StuffOf(a) is not { } x || StuffOf(b) is not { } y)
        {
            return null;
        }

        var (lo, hi) = x <= y ? (x, y) : (y, x);
        return (lo, hi) switch
        {
            (Stuff.Metal, Stuff.Metal) => ContactPair.MetalMetal,
            (Stuff.Metal, Stuff.Wood) => ContactPair.MetalWood,
            (Stuff.Wood, Stuff.Wood) => ContactPair.WoodWood,
            // 革は鉄・木に当たると、柔らかい物を硬い物で打った音（肉と同じ鳴り方）になる。
            (Stuff.Metal, Stuff.Leather or Stuff.Flesh) => ContactPair.MetalFlesh,
            (Stuff.Wood, Stuff.Leather or Stuff.Flesh) => ContactPair.WoodFlesh,
            (Stuff.Leather, Stuff.Leather) => ContactPair.GloveGlove,
            (Stuff.Leather, Stuff.Flesh) => ContactPair.GloveFlesh,
            _ => ContactPair.FleshFlesh,
        };
    }

    private static Stuff? StuffOf(byte material) => material switch
    {
        0 => null,
        BoxerAnatomy.Iron => Stuff.Metal,
        BoxerAnatomy.Wood => Stuff.Wood,
        BoxerAnatomy.RedGlove or BoxerAnatomy.BlueGlove or BoxerAnatomy.Padding or BoxerAnatomy.Boot => Stuff.Leather,
        _ => Stuff.Flesh,
    };
}
