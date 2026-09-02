namespace coreservice.Application.Interfaces;

public interface IAiSummarizeService
{
    Task<string> SummarizeAsync(string content, string courseTitle);
}