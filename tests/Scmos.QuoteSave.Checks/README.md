# Calculated quotation checks

Run the database-free calculation/validation checks (also run in API CI):

```powershell
dotnet run --project tests/Scmos.QuoteSave.Checks/Scmos.QuoteSave.Checks.csproj
```

On Windows with SQL Server LocalDB, add `-- --local-db` to test persistence,
read-back through Rate Sheet, concurrent numbering, retries, and multi-route
atomic validation. The runner creates a uniquely named `ScmosQuoteSaveTests_*`
database and removes only that database in `finally`. No application connection
strings, Azure credentials, migrations, or production data are used.

For an isolated LocalDB instance, create/start `ScmosQuoteCheck_20260905` and add
`-- --local-db --isolated`. Remove that instance after testing only if you created
it for the test. The instance name is fixed so a supplied connection string cannot
accidentally point this destructive cleanup at a real database.

The Rate Sheet keeps its existing monthly numbering: a save creates one inquiry
with one row per route and a price cell per selected vehicle. All rows share DATE
and NO. Container reefer DG combinations without a Rate Sheet column are refused,
not stored under a non-DG column.
