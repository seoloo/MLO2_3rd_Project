using System.Globalization;
using System.Net;
using System.Text;

namespace QaRunner.Reports;

/// <summary>
/// 막대 하나: 실행 한 번의 값.
/// </summary>
/// <param name="AxisLabel">가로축에 적을 짧은 이름 (처음과 마지막 막대만 표시). 예: 10/26 14:30</param>
/// <param name="Tooltip">마우스를 올렸을 때 보여 줄 설명</param>
/// <param name="Value">막대 높이가 될 값. 값이 없으면(측정 실패) null로 두고 빈칸으로 그린다</param>
/// <param name="ValueLabel">마지막 막대 위에 적을 값. 예: 1.56초</param>
public sealed record ChartBar(string AxisLabel, string Tooltip, double? Value, string ValueLabel);

/// <summary>
/// 실행 기록 추이를 보여 주는 단순한 세로 막대 차트를 SVG로 그린다. 외부 라이브러리 없이 HTML 안에 바로 넣는다.
/// 한 차트에는 한 가지 값만 그린다(축 하나). 목표가 있으면 점선으로 표시한다.
/// </summary>
public static class SvgBarChart
{
    // 대시보드의 차트 칸(2열 배치) 너비에 맞춘 크기. 글자가 늘거나 줄지 않고 거의 그대로 보이게 한다
    private const double ChartWidth = 460;
    private const double ChartHeight = 180;
    private const double PlotLeft = 48;
    private const double PlotRight = 16;
    private const double PlotTop = 22;
    private const double PlotBottom = 26;

    // 실행 횟수가 적어도 막대가 지나치게 두꺼워지지 않도록 최소 이만큼 칸을 나눈다
    private const int MinimumSlotCount = 12;
    private const double MaxBarWidth = 26;

    // 가로축 이름 두 개("10/26 14:30" 정도 길이)를 겹치지 않게 적으려면 막대들이 차지하는 폭이 이만큼은 돼야 한다
    private const double MinimumAxisLabelSpan = 170;

    /// <summary>
    /// 막대 차트 SVG를 만든다.
    /// </summary>
    /// <param name="chartTitle">화면 읽기 프로그램용 차트 이름</param>
    /// <param name="bars">왼쪽(오래된 실행)부터 오른쪽(최근 실행) 순서의 막대</param>
    /// <param name="axisMaximum">세로축 최댓값</param>
    /// <param name="unitSuffix">값 뒤에 붙일 단위. 예: %, 초</param>
    /// <param name="targetValue">목표값. 있으면 점선과 "목표" 표시를 그린다</param>
    public static string Build(string chartTitle, IReadOnlyList<ChartBar> bars, double axisMaximum, string unitSuffix, double? targetValue = null)
    {
        var plotWidth = ChartWidth - PlotLeft - PlotRight;
        var plotHeight = ChartHeight - PlotTop - PlotBottom;
        var baselineY = PlotTop + plotHeight;
        var slotWidth = plotWidth / Math.Max(bars.Count, MinimumSlotCount);
        var barWidth = Math.Min(slotWidth - 4, MaxBarWidth);

        double YFor(double value) => baselineY - Math.Clamp(value / axisMaximum, 0, 1) * plotHeight;

        var svg = new StringBuilder();
        svg.Append($"""<svg class="chart" viewBox="0 0 {N(ChartWidth)} {N(ChartHeight)}" role="img" aria-label="{Encode(chartTitle)}">""");

        // 흐린 가로 눈금선 (0, 절반, 최댓값)과 세로축 숫자
        foreach (var gridValue in new[] { 0, axisMaximum / 2, axisMaximum })
        {
            var gridY = YFor(gridValue);
            svg.Append($"""<line class="grid" x1="{N(PlotLeft)}" x2="{N(ChartWidth - PlotRight)}" y1="{N(gridY)}" y2="{N(gridY)}"/>""");
            svg.Append($"""<text class="axis" x="{N(PlotLeft - 6)}" y="{N(gridY + 4)}" text-anchor="end">{FormatNumber(gridValue)}{Encode(unitSuffix)}</text>""");
        }

        for (var barIndex = 0; barIndex < bars.Count; barIndex++)
        {
            var bar = bars[barIndex];
            var slotLeft = PlotLeft + barIndex * slotWidth;
            var barLeft = slotLeft + (slotWidth - barWidth) / 2;

            // 마우스를 올릴 수 있는 영역은 막대보다 넓게(칸 전체) 잡는다
            svg.Append($"""<g class="bar-slot"><title>{Encode(bar.Tooltip)}</title>""");
            svg.Append($"""<rect class="hit" x="{N(slotLeft)}" y="{N(PlotTop)}" width="{N(slotWidth)}" height="{N(plotHeight)}"/>""");

            if (bar.Value is > 0)
                svg.Append($"""<path class="bar" d="{RoundedTopBarPath(barLeft, YFor(bar.Value.Value), barWidth, baselineY)}"/>""");
            else if (bar.Value is null)
                svg.Append($"""<text class="axis" x="{N(barLeft + barWidth / 2)}" y="{N(baselineY - 4)}" text-anchor="middle">-</text>""");

            svg.Append("</g>");

            // 마지막 막대(가장 최근 실행)에만 값을 적는다
            if (barIndex == bars.Count - 1 && bar.Value is not null)
                svg.Append($"""<text class="value" x="{N(barLeft + barWidth / 2)}" y="{N(YFor(bar.Value.Value) - 6)}" text-anchor="middle">{Encode(bar.ValueLabel)}</text>""");

            // 가로축 이름은 처음과 마지막 막대에만 적는다.
            // 막대가 적어서 두 이름이 겹칠 만큼 가까우면 마지막(가장 최근) 이름만 적는다
            var hasRoomForBothAxisLabels = bars.Count * slotWidth >= MinimumAxisLabelSpan;
            var isFirstAxisLabel = barIndex == 0 && bars.Count > 1 && hasRoomForBothAxisLabels;
            if (isFirstAxisLabel || barIndex == bars.Count - 1)
            {
                var anchor = barIndex == 0 && bars.Count > 1 ? "start" : "end";
                var labelX = anchor == "start" ? slotLeft : slotLeft + slotWidth;
                svg.Append($"""<text class="axis" x="{N(labelX)}" y="{N(ChartHeight - 6)}" text-anchor="{anchor}">{Encode(bar.AxisLabel)}</text>""");
            }
        }

        if (targetValue is not null)
        {
            var targetY = YFor(targetValue.Value);
            svg.Append($"""<line class="target" x1="{N(PlotLeft)}" x2="{N(ChartWidth - PlotRight)}" y1="{N(targetY)}" y2="{N(targetY)}"/>""");
            svg.Append($"""<text class="target-label" x="{N(ChartWidth - PlotRight)}" y="{N(targetY - 5)}" text-anchor="end">목표 {FormatNumber(targetValue.Value)}{Encode(unitSuffix)}</text>""");
        }

        svg.Append($"""<line class="baseline" x1="{N(PlotLeft)}" x2="{N(ChartWidth - PlotRight)}" y1="{N(baselineY)}" y2="{N(baselineY)}"/>""");
        svg.Append("</svg>");
        return svg.ToString();
    }

    // 윗모서리만 둥근(반지름 4) 막대 모양. 바닥은 기준선에 딱 붙는다
    private static string RoundedTopBarPath(double left, double top, double width, double baselineY)
    {
        var radius = Math.Min(4, Math.Min(width / 2, baselineY - top));
        var right = left + width;
        return $"M{N(left)},{N(baselineY)} V{N(top + radius)} Q{N(left)},{N(top)} {N(left + radius)},{N(top)} " +
               $"H{N(right - radius)} Q{N(right)},{N(top)} {N(right)},{N(top + radius)} V{N(baselineY)} Z";
    }

    // 축·목표 숫자 표기. 정수면 소수점 없이, 아니면 한 자리까지
    private static string FormatNumber(double value) => value.ToString("0.#", CultureInfo.InvariantCulture);

    // SVG 좌표는 문화권과 상관없이 점(.)을 소수점으로 쓴다
    private static string N(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Encode(string text) => WebUtility.HtmlEncode(text);
}
