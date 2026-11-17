namespace Iskender.Management.Migrations;

/// <summary>One <c>migrate</c> operation (container contract, Command line rule 1; research R14). Owned by ISK-0007.</summary>
public abstract record MigrationOperation
{
    private MigrationOperation()
    {
    }

    /// <summary>The operation's name, as event 7006 and the command line write it.</summary>
    public abstract string Name { get; }

    /// <summary><c>migrate apply</c>: every pending migration.</summary>
    public sealed record Apply : MigrationOperation
    {
        public override string Name => "apply";
    }

    /// <summary><c>migrate reverse</c>: the latest <paramref name="Steps"/> migrations, with or without data-loss confirmation.</summary>
    public sealed record Reverse(int Steps, bool ConfirmDataLoss) : MigrationOperation
    {
        public override string Name => "reverse";
    }

    /// <summary><c>migrate status</c>: the schema state.</summary>
    public sealed record Status : MigrationOperation
    {
        public override string Name => "status";
    }
}
