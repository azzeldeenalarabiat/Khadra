START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261005062940_LegalDocumentVersions') THEN
    CREATE TABLE legal_document_versions (
        id uuid NOT NULL,
        kind character varying(30) NOT NULL,
        version_label character varying(40) NOT NULL,
        effective_from timestamp with time zone NOT NULL,
        published_at timestamp with time zone NOT NULL,
        published_by_admin_id uuid NOT NULL,
        body_en text NOT NULL,
        body_ar text NOT NULL,
        body_en_sha256 character(64) NOT NULL,
        body_ar_sha256 character(64) NOT NULL,
        updated_at timestamp with time zone,
        CONSTRAINT pk_legal_document_versions PRIMARY KEY (id),
        CONSTRAINT ck_legal_document_versions_bodies CHECK (length(body_en) BETWEEN 1 AND 200000 AND length(body_ar) BETWEEN 1 AND 200000),
        CONSTRAINT ck_legal_document_versions_effective_from CHECK (effective_from >= published_at),
        CONSTRAINT ck_legal_document_versions_hashes CHECK (length(body_en_sha256) = 64 AND length(body_ar_sha256) = 64),
        CONSTRAINT ck_legal_document_versions_kind CHECK (kind IN ('Terms', 'Privacy')),
        CONSTRAINT ck_legal_document_versions_version_label CHECK (length(version_label) BETWEEN 1 AND 40 AND version_label = trim(version_label))
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261005062940_LegalDocumentVersions') THEN
    CREATE UNIQUE INDEX ux_legal_document_versions_kind_effective_from ON legal_document_versions (kind, effective_from);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261005062940_LegalDocumentVersions') THEN
    CREATE UNIQUE INDEX ux_legal_document_versions_kind_version_label ON legal_document_versions (kind, version_label);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261005062940_LegalDocumentVersions') THEN

    CREATE TRIGGER legal_document_versions_append_only
    BEFORE UPDATE OR DELETE ON legal_document_versions
    FOR EACH ROW EXECUTE FUNCTION khadra_table_is_append_only();

    CREATE TRIGGER legal_document_versions_no_truncate
    BEFORE TRUNCATE ON legal_document_versions
    FOR EACH STATEMENT EXECUTE FUNCTION khadra_table_is_append_only();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261005062940_LegalDocumentVersions') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20261005062940_LegalDocumentVersions', '10.0.11');
    END IF;
END $EF$;
COMMIT;

