using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.Fonts;

namespace ShantiBotDi.Services;

public interface IChartRendererService
{
    Task<byte[]> RenderTradingChartAsync(List<CandleData> candles, TradingMetrics metrics, string language = "en");
}

public class ChartRendererService : IChartRendererService
{
    private const int ChartWidth = 900;
    private const int ChartHeight = 500;
    private const int Padding = 50;
    private const int CandleWidth = 8;
    private const int CandleSpacing = 3;

    private readonly Font _titleFont;
    private readonly Font _metricsFont;
    private readonly Font _smallFont;
    private readonly ILocalizationService _localizationService;

    public ChartRendererService(ILocalizationService localizationService)
    {
        _localizationService = localizationService;
        var fontFamily = SystemFonts.TryGet("Arial", out FontFamily arial)
            ? arial
            : SystemFonts.Families.First();

        _titleFont = new Font(fontFamily, 16, FontStyle.Bold);
        _metricsFont = new Font(fontFamily, 11);
        _smallFont = new Font(fontFamily, 9);
    }

    public Task<byte[]> RenderTradingChartAsync(List<CandleData> candles, TradingMetrics metrics, string language = "en")
    {
        using var image = new Image<Rgba32>(ChartWidth, ChartHeight, new Rgba32(15, 15, 25, 255));

        image.Mutate(ctx =>
        {
            // Градієнтний фон
            DrawGradientBackground(ctx);
            
            // Сітка з приглушеним стилем
            DrawElegantGrid(ctx, metrics);
            
            // Свічки
            if (candles.Count > 0)
            {
                DrawElegantCandles(ctx, candles, metrics);
            }

            // Інформація про прибуток
            DrawProfitInfo(ctx, metrics, language);
        });

        using var ms = new MemoryStream();
        image.SaveAsPng(ms);
        ms.Seek(0, SeekOrigin.Begin);
        return Task.FromResult(ms.ToArray());
    }

    private void DrawGradientBackground(IImageProcessingContext ctx)
    {
        // Темний градієнт для професійного вигляду
        for (int y = 0; y < ChartHeight; y++)
        {
            float ratio = (float)y / ChartHeight;
            var color = new Rgba32(
                (byte)(15 + (25 * ratio)),
                (byte)(15 + (25 * ratio)),
                (byte)(25 + (40 * ratio)),
                255
            );
            
            var line = new Polygon(new LinearLineSegment(
                new PointF(0, y),
                new PointF(ChartWidth, y)
            ));
            ctx.Draw(Pens.Solid(color, 1), line);
        }
    }

    private void DrawElegantGrid(IImageProcessingContext ctx, TradingMetrics metrics)
    {
        var gridColor = new Rgba32(60, 60, 80, 40);
        var gridPen = Pens.Solid(gridColor, 1);

        // Горизонтальні лінії
        for (int i = 0; i <= 4; i++)
        {
            float y = Padding + (ChartHeight - 2 * Padding) * i / 4f;
            var line = new Polygon(new LinearLineSegment(
                new PointF(Padding, y),
                new PointF(ChartWidth - Padding, y)
            ));
            ctx.Draw(gridPen, line);

            // Значення на осі Y (баланс)
            decimal priceRange = metrics.HighPrice - metrics.LowPrice;
            decimal currentPrice = metrics.LowPrice + (priceRange * (1m - (decimal)i / 4m));
            string priceLabel = $"${currentPrice:F0}";
            
            ctx.DrawText(
                new RichTextOptions(_smallFont) { Origin = new PointF(10, y - 5) },
                priceLabel,
                new Rgba32(150, 150, 170, 180)
            );
        }

        // Вертикальні лінії
        float chartWidth = ChartWidth - 2 * Padding;
        for (int i = 0; i <= 5; i++)
        {
            float x = Padding + chartWidth * i / 5f;
            var line = new Polygon(new LinearLineSegment(
                new PointF(x, Padding),
                new PointF(x, ChartHeight - Padding)
            ));
            ctx.Draw(gridPen, line);
        }

        // Осі X та Y
        var axisColor = new Rgba32(100, 100, 130, 150);
        var axisPen = Pens.Solid(axisColor, 2);
        
        var yAxis = new Polygon(new LinearLineSegment(
            new PointF(Padding, Padding),
            new PointF(Padding, ChartHeight - Padding)
        ));
        ctx.Draw(axisPen, yAxis);

        var xAxis = new Polygon(new LinearLineSegment(
            new PointF(Padding, ChartHeight - Padding),
            new PointF(ChartWidth - Padding, ChartHeight - Padding)
        ));
        ctx.Draw(axisPen, xAxis);
    }

    private void DrawElegantCandles(IImageProcessingContext ctx, List<CandleData> candles, TradingMetrics metrics)
    {
        decimal highPrice = metrics.HighPrice;
        decimal lowPrice = metrics.LowPrice;
        decimal priceRange = highPrice - lowPrice;
        if (priceRange == 0) priceRange = 1;

        float chartWidth = ChartWidth - 2 * Padding;
        float chartHeight = ChartHeight - 2 * Padding;
        float candleWidthWithSpacing = CandleWidth + CandleSpacing;
        int visibleCandles = Math.Min(candles.Count, (int)(chartWidth / candleWidthWithSpacing));
        int startIndex = Math.Max(0, candles.Count - visibleCandles);
        var displayCandles = candles.Skip(startIndex).ToList();

        // Сліди для гладкості
        var trailColor = new Rgba32(100, 200, 100, 30);
        var trailPoints = new List<PointF>();

        for (int i = 0; i < displayCandles.Count; i++)
        {
            var candle = displayCandles[i];
            float x = Padding + (i * candleWidthWithSpacing) + CandleWidth / 2f;

            float openNorm = (float)((candle.Open - lowPrice) / priceRange);
            float closeNorm = (float)((candle.Close - lowPrice) / priceRange);
            float highNorm = (float)((candle.High - lowPrice) / priceRange);
            float lowNorm = (float)((candle.Low - lowPrice) / priceRange);

            float highY = Padding + chartHeight * (1 - highNorm);
            float lowY = Padding + chartHeight * (1 - lowNorm);
            float openY = Padding + chartHeight * (1 - openNorm);
            float closeY = Padding + chartHeight * (1 - closeNorm);

            // Все зелене - прибуток
            var bodyColor = new Rgba32(76, 175, 80, 255);      // Яскравий зелений
            var shadowColor = new Rgba32(56, 142, 60, 200);    // Темніший зелений

            // Фітіль
            var shadowPen = Pens.Solid(shadowColor, 1.5f);
            var wick = new Polygon(new LinearLineSegment(new PointF(x, highY), new PointF(x, lowY)));
            ctx.Draw(shadowPen, wick);

            // Тіло свічки
            float bodyTop = Math.Min(openY, closeY);
            float bodyBottom = Math.Max(openY, closeY);
            float bodyHeight = Math.Max(2, bodyBottom - bodyTop);

            var rect = new RectangleF(x - CandleWidth / 2f, bodyTop, CandleWidth, bodyHeight);
            ctx.Fill(bodyColor, rect);
            ctx.Draw(Pens.Solid(new Rgba32(100, 200, 100, 150), 0.5f), rect);

            // Збираємо точки для лінії тренду
            trailPoints.Add(new PointF(x, closeY));
        }

        // Рисуємо лінію тренду
        if (trailPoints.Count > 1)
        {
            for (int i = 0; i < trailPoints.Count - 1; i++)
            {
                var line = new Polygon(new LinearLineSegment(trailPoints[i], trailPoints[i + 1]));
                ctx.Draw(Pens.Solid(trailColor, 2), line);
            }
        }
    }

    private void DrawProfitInfo(IImageProcessingContext ctx, TradingMetrics metrics, string language)
    {
        var normalizedLanguage = _localizationService.NormalizeCode(language);
        var backgroundColor = new Rgba32(25, 25, 40, 200);
        var textColor = new Rgba32(255, 255, 255, 255);
        var profitColor = new Rgba32(76, 175, 80, 255);

        // Фон для інформації
        var infoPanelRect = new RectangleF(ChartWidth - 320, 10, 310, 105);
        ctx.Fill(backgroundColor, infoPanelRect);
        ctx.Draw(Pens.Solid(profitColor, 1), infoPanelRect);

        // Заголовок
        string title = _localizationService.GetText(normalizedLanguage, "chart.title");
        ctx.DrawText(
            new RichTextOptions(_titleFont) { Origin = new PointF(ChartWidth - 310, 20) },
            title,
            profitColor
        );

        // Інформація про прибуток
        decimal profitAmount = metrics.ProfitLoss;
        decimal profitPercent = metrics.ChangePercent;
        decimal newBalance = metrics.CurrentPrice;

        string profitLabel = _localizationService.GetText(normalizedLanguage, "chart.profit");
        string balanceLabel = _localizationService.GetText(normalizedLanguage, "chart.balance");

        ctx.DrawText(
            new RichTextOptions(_metricsFont) { Origin = new PointF(ChartWidth - 310, 50) },
            $"{profitLabel} ${profitAmount:F2}",
            textColor
        );

        ctx.DrawText(
            new RichTextOptions(_metricsFont) { Origin = new PointF(ChartWidth - 310, 75) },
            $"{balanceLabel} ${newBalance:F2}",
            new Rgba32(255, 193, 7, 255)
        );

        // Легенда внизу
        string legend = _localizationService.GetText(normalizedLanguage, "chart.legend");
        
        ctx.DrawText(
            new RichTextOptions(_smallFont) { Origin = new PointF(Padding, ChartHeight - 20) },
            legend,
            new Rgba32(150, 150, 170, 200)
        );
    }
    
}