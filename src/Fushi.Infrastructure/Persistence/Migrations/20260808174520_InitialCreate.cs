using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fushi.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class _20260808174520_InitialCreate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "audit_entries",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                guild_id = table.Column<long>(type: "bigint", nullable: false),
                scope = table.Column<int>(type: "integer", nullable: false),
                action = table.Column<int>(type: "integer", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                created_by = table.Column<long>(type: "bigint", nullable: false),
                subject_id = table.Column<Guid>(type: "uuid", nullable: true),
                subject_code = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: true),
                target_id = table.Column<long>(type: "bigint", nullable: true),
                reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                metadata = table.Column<string>(type: "jsonb", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_audit_entries", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "cycles",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                code = table.Column<string>(type: "character(6)", fixedLength: true, maxLength: 6, nullable: false),
                guild_id = table.Column<long>(type: "bigint", nullable: false),
                scheduled_date = table.Column<DateOnly>(type: "date", nullable: false),
                opens_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                closes_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                announcement_message_id = table.Column<long>(type: "bigint", nullable: true),
                results_message_id = table.Column<long>(type: "bigint", nullable: true),
                allow_abstain = table.Column<bool>(type: "boolean", nullable: false),
                allow_self_vote = table.Column<bool>(type: "boolean", nullable: false),
                allow_vote_change = table.Column<bool>(type: "boolean", nullable: false),
                approval_ratio = table.Column<double>(type: "double precision", nullable: false),
                quorum = table.Column<int>(type: "integer", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                created_by = table.Column<long>(type: "bigint", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                updated_by = table.Column<long>(type: "bigint", nullable: true),
                deleted_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                deleted_by = table.Column<long>(type: "bigint", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_cycles", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "guilds",
            columns: table => new
            {
                id = table.Column<long>(type: "bigint", nullable: false),
                is_enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                archive_channel_id = table.Column<long>(type: "bigint", nullable: true),
                intake_channel_id = table.Column<long>(type: "bigint", nullable: true),
                log_channel_id = table.Column<long>(type: "bigint", nullable: true),
                results_channel_id = table.Column<long>(type: "bigint", nullable: true),
                review_channel_id = table.Column<long>(type: "bigint", nullable: true),
                allow_abstain = table.Column<bool>(type: "boolean", nullable: false),
                allow_self_vote = table.Column<bool>(type: "boolean", nullable: false),
                allow_vote_change = table.Column<bool>(type: "boolean", nullable: false),
                approval_ratio = table.Column<double>(type: "double precision", nullable: false),
                quorum = table.Column<int>(type: "integer", nullable: false),
                closes_at = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                cycle_days = table.Column<int>(type: "integer", nullable: false),
                opens_at = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                time_zone_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                created_by = table.Column<long>(type: "bigint", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                updated_by = table.Column<long>(type: "bigint", nullable: true),
                deleted_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                deleted_by = table.Column<long>(type: "bigint", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_guilds", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "submissions",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                code = table.Column<string>(type: "character(6)", fixedLength: true, maxLength: 6, nullable: false),
                guild_id = table.Column<long>(type: "bigint", nullable: false),
                cycle_id = table.Column<Guid>(type: "uuid", nullable: true),
                applicant_id = table.Column<long>(type: "bigint", nullable: false),
                source_channel_id = table.Column<long>(type: "bigint", nullable: false),
                source_message_id = table.Column<long>(type: "bigint", nullable: false),
                title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                content = table.Column<string>(type: "character varying(3800)", maxLength: 3800, nullable: false),
                review_message_id = table.Column<long>(type: "bigint", nullable: true),
                thread_id = table.Column<long>(type: "bigint", nullable: true),
                status = table.Column<int>(type: "integer", nullable: false),
                outcome = table.Column<int>(type: "integer", nullable: true),
                decided_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                decided_by = table.Column<long>(type: "bigint", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                created_by = table.Column<long>(type: "bigint", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                updated_by = table.Column<long>(type: "bigint", nullable: true),
                deleted_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                deleted_by = table.Column<long>(type: "bigint", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_submissions", x => x.id);
                table.ForeignKey(
                    name: "fk_submissions_cycles_cycle_id",
                    column: x => x.cycle_id,
                    principalTable: "cycles",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateTable(
            name: "voting_permissions",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                guild_id = table.Column<long>(type: "bigint", nullable: false),
                scope = table.Column<int>(type: "integer", nullable: false),
                target_id = table.Column<long>(type: "bigint", nullable: false),
                note = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                created_by = table.Column<long>(type: "bigint", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                updated_by = table.Column<long>(type: "bigint", nullable: true),
                deleted_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                deleted_by = table.Column<long>(type: "bigint", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_voting_permissions", x => x.id);
                table.ForeignKey(
                    name: "fk_voting_permissions_guilds_guild_id",
                    column: x => x.guild_id,
                    principalTable: "guilds",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "votes",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                submission_id = table.Column<Guid>(type: "uuid", nullable: false),
                voter_id = table.Column<long>(type: "bigint", nullable: false),
                choice = table.Column<int>(type: "integer", nullable: false),
                comment = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                revision_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                created_by = table.Column<long>(type: "bigint", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                updated_by = table.Column<long>(type: "bigint", nullable: true),
                deleted_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                deleted_by = table.Column<long>(type: "bigint", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_votes", x => x.id);
                table.ForeignKey(
                    name: "fk_votes_submissions_submission_id",
                    column: x => x.submission_id,
                    principalTable: "submissions",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_audit_entries_guild_created",
            table: "audit_entries",
            columns: new[] { "guild_id", "created_at" },
            descending: new[] { false, true });

        migrationBuilder.CreateIndex(
            name: "ix_audit_entries_guild_scope_created",
            table: "audit_entries",
            columns: new[] { "guild_id", "scope", "created_at" },
            descending: new[] { false, false, true });

        migrationBuilder.CreateIndex(
            name: "ix_audit_entries_guild_subject_code",
            table: "audit_entries",
            columns: new[] { "guild_id", "subject_code" });

        migrationBuilder.CreateIndex(
            name: "ix_cycles_guild_code",
            table: "cycles",
            columns: new[] { "guild_id", "code" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_cycles_guild_date",
            table: "cycles",
            columns: new[] { "guild_id", "scheduled_date" },
            unique: true,
            filter: "deleted_at IS NULL");

        migrationBuilder.CreateIndex(
            name: "ix_cycles_one_open_per_guild",
            table: "cycles",
            column: "guild_id",
            unique: true,
            filter: "status = 1 AND deleted_at IS NULL");

        migrationBuilder.CreateIndex(
            name: "ix_submissions_cycle",
            table: "submissions",
            column: "cycle_id");

        migrationBuilder.CreateIndex(
            name: "ix_submissions_guild_code",
            table: "submissions",
            columns: new[] { "guild_id", "code" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_submissions_guild_source_message",
            table: "submissions",
            columns: new[] { "guild_id", "source_message_id" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_submissions_guild_status_created",
            table: "submissions",
            columns: new[] { "guild_id", "status", "created_at" });

        migrationBuilder.CreateIndex(
            name: "ix_votes_submission_voter_live",
            table: "votes",
            columns: new[] { "submission_id", "voter_id" },
            unique: true,
            filter: "deleted_at IS NULL");

        migrationBuilder.CreateIndex(
            name: "ix_voting_permissions_guild_target_live",
            table: "voting_permissions",
            columns: new[] { "guild_id", "scope", "target_id" },
            unique: true,
            filter: "deleted_at IS NULL");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "audit_entries");

        migrationBuilder.DropTable(
            name: "votes");

        migrationBuilder.DropTable(
            name: "voting_permissions");

        migrationBuilder.DropTable(
            name: "submissions");

        migrationBuilder.DropTable(
            name: "guilds");

        migrationBuilder.DropTable(
            name: "cycles");
    }
}
