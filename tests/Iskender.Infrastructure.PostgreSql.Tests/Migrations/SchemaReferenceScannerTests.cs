using Iskender.Infrastructure.PostgreSql.Migrations;

namespace Iskender.Infrastructure.PostgreSql.Tests.Migrations;

public class SchemaReferenceScannerTests
{
    [Theory]
    [InlineData("SELECT * FROM isk_beta.t", "isk_beta")]
    [InlineData("SELECT * FROM \"isk_beta\".t", "isk_beta")]
    [InlineData("SELECT * FROM ISK_BETA.t", "isk_beta")]
    [InlineData("SELECT * FROM Isk_Beta.t", "isk_beta")]
    [InlineData("SELECT * FROM public.t", "public")]
    [InlineData("SELECT * FROM PUBLIC.t", "public")]
    [InlineData("SELECT * FROM \"public\".t", "public")]
    [InlineData("SELECT * FROM isk_beta . t", "isk_beta")]
    [InlineData("SELECT * FROM isk_beta/* c */.t", "isk_beta")]
    [InlineData("SELECT * FROM isk_beta -- c\n.t", "isk_beta")]
    [InlineData("SELECT * FROM db.isk_beta.t", "isk_beta")]
    [InlineData("CREATE TABLE t (id int REFERENCES isk_beta.counters(id))", "isk_beta")]
    [InlineData("SELECT 'x', isk_beta.f($1)", "isk_beta")]
    [InlineData("SELECT $$x$$, isk_beta.t", "isk_beta")]
    public void ASchemaQualifierIsReported(string sql, string schema)
    {
        Assert.Equal([schema], SchemaReferenceScanner.Scan(sql));
        Assert.True(SchemaReferenceScanner.ReferencesOtherSchema(sql, "isk_alpha"));
    }

    [Theory]
    [InlineData("-- isk_beta.t\nSELECT 1")]
    [InlineData("/* isk_beta.t */ SELECT 1")]
    [InlineData("/* outer /* isk_beta.t */ still isk_beta.t */ SELECT 1")]
    [InlineData("SELECT 'isk_beta.t'")]
    [InlineData("SELECT 'it''s isk_beta.t'")]
    [InlineData("SELECT E'\\' isk_beta.t'")]
    [InlineData("SELECT e'isk_beta.t\\''")]
    [InlineData("SELECT $$ isk_beta.t $$")]
    [InlineData("SELECT $body$ isk_beta.t $$ isk_beta.t $body$")]
    [InlineData("CREATE FUNCTION f() RETURNS int LANGUAGE sql AS $fn$ SELECT 1 FROM isk_beta.t $fn$")]
    [InlineData("SELECT \"isk_beta.t\"")]
    [InlineData("SELECT \"x\"\"isk_beta.t\"")]
    [InlineData("SELECT isk_beta")]
    [InlineData("SELECT t.isk_beta FROM t")]
    [InlineData("SELECT public")]
    [InlineData("SELECT \"ISK_BETA\".t")]
    [InlineData("SELECT x_isk_beta.t")]
    [InlineData("SELECT $1, isk_beta")]
    public void AReferenceInASkippedConstructOrNotAQualifierIsIgnored(string sql)
    {
        Assert.Empty(SchemaReferenceScanner.Scan(sql));
        Assert.False(SchemaReferenceScanner.ReferencesOtherSchema(sql, "isk_alpha"));
    }

    [Fact]
    public void ItsOwnSchemaIsAllowedAndEveryOtherIsFound()
    {
        const string sql = "CREATE TABLE isk_alpha.widgets (); GRANT SELECT ON isk_alpha.widgets TO r; SELECT isk_beta.x, public.y";

        Assert.Equal(["isk_alpha", "isk_alpha", "isk_beta", "public"], SchemaReferenceScanner.Scan(sql));
        Assert.True(SchemaReferenceScanner.ReferencesOtherSchema(sql, "isk_alpha"));
        Assert.False(SchemaReferenceScanner.ReferencesOtherSchema("CREATE TABLE isk_alpha.widgets ()", "isk_alpha"));
        Assert.False(SchemaReferenceScanner.ReferencesOtherSchema("CREATE TABLE widgets ()", "isk_alpha"));
    }

    [Fact]
    public void AnUnterminatedConstructEndsTheScan()
    {
        Assert.Empty(SchemaReferenceScanner.Scan("SELECT 'isk_beta.t"));
        Assert.Empty(SchemaReferenceScanner.Scan("/* isk_beta.t"));
        Assert.Empty(SchemaReferenceScanner.Scan("SELECT $q$ isk_beta.t"));
        Assert.Empty(SchemaReferenceScanner.Scan("SELECT \"isk_beta.t"));
    }

    [Fact]
    public void KnownLimitANameBuiltAtRunTimeIsNotSeen()
    {
        // Research R11's known limit: a schema name assembled inside dynamic SQL is invisible to the lexer.
        // Migration review covers it, and the contract says so.
        const string sql = "DO $$ BEGIN EXECUTE format('SELECT * FROM %I.t', 'isk_' || 'beta'); END $$";

        Assert.Empty(SchemaReferenceScanner.Scan(sql));
    }
}
