using Dbms.Core.Models;

namespace Dbms.App.ViewModels;

public sealed class TableComparisonRow : ObservableObject
{
    private bool isSelected;

    public TableComparisonRow(TableComparison comparison)
    {
        Comparison = comparison;
        isSelected = comparison.SourceTable is not null && comparison.DestinationTable is not null;
    }

    public TableComparison Comparison { get; }
    public string Name => Comparison.Name;
    public string Status => Comparison.StatusText;
    public string DifferenceSummary => Comparison.Differences.Count == 0 ? "No differences" : string.Join(" ", Comparison.Differences.Take(2));
    public string SourceColumns => Comparison.SourceTable is null ? "—" : string.Join(", ", Comparison.SourceTable.Columns.Select(column => column.Name));
    public string DestinationColumns => Comparison.DestinationTable is null ? "—" : string.Join(", ", Comparison.DestinationTable.Columns.Select(column => column.Name));
    public bool IsTransferCandidate => Comparison.SourceTable is not null && Comparison.DestinationTable is not null;

    public bool IsSelected
    {
        get => isSelected;
        set => SetProperty(ref isSelected, value);
    }
}

public sealed class TransferTableRow
{
    public TransferTableRow(TransferTablePlan plan)
    {
        Plan = plan;
    }

    public TransferTablePlan Plan { get; }
    public string TableName => Plan.SourceTable.QualifiedName;
    public string RowsToInsert => Plan.RowsToInsert.ToString("N0");
    public string RowsToSkip => Plan.RowsToSkip.ToString("N0");
    public string Conflicts => Plan.ConflictCount.ToString("N0");
    public string Status => Plan.CanExecute ? "Ready" : "Blocked";
    public string Message => Plan.Message;
    public string Mappings => Plan.Mappings.Count == 0
        ? "—"
        : string.Join(", ", Plan.Mappings.Select(mapping => $"{mapping.SourceColumn} → {mapping.DestinationColumn}"));
}

public sealed class RowDifferenceRow
{
    public RowDifferenceRow(RowDifference difference)
    {
        Difference = difference;
    }

    public RowDifference Difference { get; }
    public string Kind => Difference.KindText;
    public string Key => Difference.Key;
    public string Source => Difference.SourceSummary;
    public string Destination => Difference.DestinationSummary;
}
