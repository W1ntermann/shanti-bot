using ShantiBotDi.Models;

namespace ShantiBotDi.Helpers;

public static class InvestmentHelper
{
    private static readonly Random _random = new();

    public static string GetDurationText(InvestmentDuration duration) => duration switch
    {
        InvestmentDuration.TwoDays => "2 Days",
        InvestmentDuration.OneWeek => "1 Week",
        InvestmentDuration.TwoWeeks => "2 Weeks",
        _ => "Unknown"
    };
    
    public static decimal GetRandomPercent(InvestmentDuration duration)
    {
        return duration switch
        {
            InvestmentDuration.TwoDays => RandomDecimal(1.0m, 2.2m),  // 1.0 - 3.0%
            InvestmentDuration.OneWeek => RandomDecimal(8.0m, 11.5m),       // 8.0 - 13.0%
            InvestmentDuration.TwoWeeks => RandomDecimal(22.0m, 26.0m),      // 22.0 - 30.0%
            _ => 0m
        };
    }
    
    private static decimal RandomDecimal(decimal min, decimal max)
    {

        double sample = _random.NextDouble();
        decimal scaled = min + (decimal)sample * (max - min);
        return Math.Round(scaled, 2);
    }
}