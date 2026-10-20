namespace Verdant.Core;

// Razer Protocol 2.5 macro encoding, based on the MIT-licensed Secretarybird
// implementation. See THIRD-PARTY-NOTICES.txt. USB usages are not Windows VKs.
public static class OnboardMacro
{
    public static byte Usage(int vk) => vk switch
    {
        >=65 and <=90 => (byte)(vk-65+4),
        >=49 and <=57 => (byte)(vk-49+30),
        48=>39, 13=>40, 27=>41, 8=>42, 9=>43, 32=>44,
        >=112 and <=123 => (byte)(vk-112+58),
        17 or 0xA2=>0xE0, 16 or 0xA0=>0xE1, 18 or 0xA4=>0xE2, 0x5B=>0xE3,
        0xA3=>0xE4, 0xA1=>0xE5, 0xA5=>0xE6, 0x5C=>0xE7,
        _=>throw new InvalidDataException("On-mouse playback supports letters, digits, F1–F12, modifiers, Space, Enter, Tab, Escape and Backspace.")
    };
    public static byte[] Encode(Binding b, bool separateMouseAndKey=false)
    {
        if(b.Kind!=ActionKind.Mouse || b.Mouse!=ButtonId.Forward || b.Mode!=PlayMode.Toggle || !b.ExtraKeyEnabled)
            throw new InvalidDataException("On-mouse playback needs the Forward mouse toggle and an enabled extra key.");
        if(b.ExtraKeys.Count==0 || b.ExtraKeys.Count>6 || b.ExtraIntervalMs<100 || b.ExtraIntervalMs>60000 ||
           b.ExtraPressMs<10 || b.ExtraPressMs>1000 || b.ExtraPressMs>b.ExtraIntervalMs-10)
            throw new InvalidDataException("Invalid on-mouse key timing or shortcut.");
        var keys=b.ExtraKeys.Select(Usage).Distinct().ToArray();
        var data=new List<byte> {8,16}; // Keep Mouse5 held across device-controlled iterations.
        // Avoid delivering mouse activation and the keyboard shortcut in the
        // same input batch. Keep the requested key duration and overall period.
        int lead=separateMouseAndKey ? Math.Min(100,(b.ExtraIntervalMs-b.ExtraPressMs)/2) : 0;
        if(lead>0) Delay(data,lead);
        foreach(var key in keys) data.AddRange([1,key]);
        Delay(data,b.ExtraPressMs);
        foreach(var key in keys.Reverse()) data.AddRange([2,key]);
        Delay(data,b.ExtraIntervalMs-b.ExtraPressMs-lead);
        return data.ToArray();
    }
    private static void Delay(List<byte> data,int ms)
    {
        if(ms<=255) data.AddRange([17,(byte)ms]);
        else data.AddRange([18,(byte)(ms>>8),(byte)ms]);
    }
    public static byte[] EncodeKeyboard(Binding b) => Encode(b).Skip(2).ToArray();
    public static byte[] EncodeBoundedKeyTest(Binding b, int taps)
    {
        if(taps is <1 or >6 || b.ExtraIntervalMs>1000)
            throw new InvalidDataException("Hardware diagnostics are limited to six taps over six seconds.");
        // Reuse key validation and timing, but exclude the experimental mouse-down.
        var tap=EncodeKeyboard(b);
        return Enumerable.Range(0,taps).SelectMany(_=>tap).ToArray();
    }
    public static byte[] EncodeBoundedHoldTest(Binding b,int taps) =>
        new byte[]{8,16}.Concat(EncodeBoundedKeyTest(b,taps)).Concat(new byte[]{8,0}).ToArray();
    public static byte[] EncodeBoundedToggleTest(Binding b,int taps)
    {
        _=EncodeBoundedKeyTest(b,taps); // Enforce the same hard duration/count limits.
        return Enumerable.Range(0,taps).SelectMany(_=>Encode(b)).Concat(new byte[]{8,0}).ToArray();
    }
}
