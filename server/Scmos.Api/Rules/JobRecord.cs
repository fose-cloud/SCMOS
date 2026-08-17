using System.Text.Json;
using System.Text.Json.Serialization;

namespace Scmos.Api.Rules;

/// <summary>
/// One operational job, as the register stores it.
///
/// Only the fields the rules read are named. The workspace's job model owns
/// forty-odd others and they change together; naming them all here would make
/// this file something that has to be edited every time a column is renamed on
/// a screen, which is exactly the coupling the JSON column was chosen to avoid.
///
/// Everything is a string because everything in the plan is a string — the
/// whole point of the data standard is that a date only becomes a date once it
/// has been judged.
/// </summary>
public class JobRecord
{
    [JsonPropertyName("key")] public string Key { get; set; } = "";
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("cat")] public string Cat { get; set; } = "";
    [JsonPropertyName("op")] public string Op { get; set; } = "";
    [JsonPropertyName("opId")] public string OpId { get; set; } = "";
    [JsonPropertyName("date")] public string Date { get; set; } = "";
    [JsonPropertyName("customer")] public string Customer { get; set; } = "";
    [JsonPropertyName("trucker")] public string Trucker { get; set; } = "";
    [JsonPropertyName("jobCode")] public string JobCode { get; set; } = "";
    [JsonPropertyName("abs")] public string Abs { get; set; } = "";
    [JsonPropertyName("jobNo")] public string JobNo { get; set; } = "";
    [JsonPropertyName("planTime")] public string PlanTime { get; set; } = "";
    [JsonPropertyName("arrDate")] public string ArrDate { get; set; } = "";
    [JsonPropertyName("arrTime")] public string ArrTime { get; set; } = "";
    [JsonPropertyName("closingDate")] public string ClosingDate { get; set; } = "";
    [JsonPropertyName("closingTime")] public string ClosingTime { get; set; } = "";
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("container")] public string Container { get; set; } = "";
    [JsonPropertyName("licence")] public string Licence { get; set; } = "";
    [JsonPropertyName("driver")] public string Driver { get; set; } = "";
    [JsonPropertyName("contact")] public string Contact { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("weight")] public string Weight { get; set; } = "";
    [JsonPropertyName("reason")] public string Reason { get; set; } = "";
    [JsonPropertyName("seal")] public string Seal { get; set; } = "";

    /// <summary>The job's own key, falling back to the id older rows were saved with.</summary>
    public string Identity => Key.Length > 0 ? Key : Id;

    /// <summary>What an operator calls this job.</summary>
    public string Reference =>
        JobCode.Length > 0 ? JobCode : Abs.Length > 0 ? Abs : JobNo.Length > 0 ? JobNo : Customer;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    /// <summary>
    /// Reads a stored row. A row that will not parse is skipped rather than
    /// breaking the load, the same as everywhere else in the system.
    /// </summary>
    public static JobRecord? From(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<JobRecord>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
