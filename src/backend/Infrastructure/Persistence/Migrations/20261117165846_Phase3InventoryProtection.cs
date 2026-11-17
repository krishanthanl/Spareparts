using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase3InventoryProtection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION inv_draft_lines_only() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE parent_status int;
                DECLARE parent_id uuid;
                BEGIN
                    -- Reparenting a line must not bypass its original document's lock.
                    -- Draft editors replace lines instead of moving them between documents.
                    IF TG_OP = 'UPDATE' AND (to_jsonb(OLD) ->> TG_ARGV[1]) IS DISTINCT FROM (to_jsonb(NEW) ->> TG_ARGV[1]) THEN
                        RAISE EXCEPTION 'Document lines cannot change parent (%).', TG_TABLE_NAME USING ERRCODE = 'P0001';
                    END IF;
                    IF TG_OP = 'DELETE' THEN
                        parent_id := (to_jsonb(OLD) ->> TG_ARGV[1])::uuid;
                    ELSE
                        parent_id := (to_jsonb(NEW) ->> TG_ARGV[1])::uuid;
                    END IF;
                    -- Serialize draft edits against posting, not just against other edits.
                    EXECUTE format('SELECT "Status" FROM %I WHERE "Id" = $1 FOR UPDATE', TG_ARGV[0]) INTO parent_status USING parent_id;
                    IF parent_status IS DISTINCT FROM 1 THEN
                        RAISE EXCEPTION 'Lines of a posted or cancelled document cannot change (%).', TG_TABLE_NAME USING ERRCODE = 'P0001';
                    END IF;
                    IF TG_OP = 'DELETE' THEN RETURN OLD; END IF;
                    RETURN NEW;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION inv_draft_lines_only() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE parent_status int;
                DECLARE parent_id uuid;
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        parent_id := (to_jsonb(OLD) ->> TG_ARGV[1])::uuid;
                    ELSE
                        parent_id := (to_jsonb(NEW) ->> TG_ARGV[1])::uuid;
                    END IF;
                    EXECUTE format('SELECT "Status" FROM %I WHERE "Id" = $1', TG_ARGV[0]) INTO parent_status USING parent_id;
                    IF parent_status IS DISTINCT FROM 1 THEN
                        RAISE EXCEPTION 'Lines of a posted or cancelled document cannot change (%).', TG_TABLE_NAME USING ERRCODE = 'P0001';
                    END IF;
                    IF TG_OP = 'DELETE' THEN RETURN OLD; END IF;
                    RETURN NEW;
                END $$;
                """);
        }
    }
}
