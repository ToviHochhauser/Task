namespace backend.Services;

public interface ITimeService
{
    Task<DateTime> GetZurichTimeAsync();
}
