namespace CodexDreamSkin;

internal static class ThemeDefinitions
{
    internal sealed record Definition(string Id, string Name, string Description, string File, string Effect);
    internal static IReadOnlyList<Definition> Definitions { get; } =
    [
        new("center-shrine", "不知火 · 神社双焰", "中央全身 · 蓝红旋焰 · 风雨蝶群", "center-shrine.png", "shrine"),
        new("center-storm", "不知火 · 天雷夜雨", "中央全身 · 雷电风暴 · 飞雨流火", "center-storm.png", "storm"),
        new("center-tide", "不知火 · 月海潮汐", "中央全身 · 推进浪潮 · 蓝焰蝶舞", "center-tide.png", "tide"),
        new("center-eclipse", "不知火 · 赤月轮舞", "中央全身 · 赤金火轮 · 旋转蝶烬", "center-eclipse.png", "eclipse"),
        new("shiranui-fire", "不知火 · 离岛焰舞", "暖金蝶火 · 海面流光 · 灯影呼吸", "shiranui-fire.png", "fire"),
        new("shiranui-moon", "不知火 · 月海流萤", "月色海雾 · 蓝蝶流萤 · 银色涟漪", "shiranui-moon.png", "moon"),
        new("shiranui-sakura", "不知火 · 樱雨花见", "樱瓣飘落 · 绯色柔雾 · 暖灯浮光", "shiranui-sakura.png", "sakura"),
        new("shiranui-crimson", "不知火 · 赤月蝶烬", "赤月夜色 · 金色余烬 · 炽焰光晕", "shiranui-crimson.png", "crimson"),
        new("silver-night", "银夜流光", "银发幻想 · 微雪漂浮 · 冷光星尘", "silver-night.png", "snow"),
        new("aurora", "极光幻境", "渐变流云 · 青蓝极光 · 静谧光点", "silver-night.png", "aurora")
    ];
}
