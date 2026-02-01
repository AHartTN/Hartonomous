-- UCD Database Schema - Standalone Unicode Character Database Storage
-- This is SEPARATE from Hartonomous - it's a data source

CREATE SCHEMA IF NOT EXISTS ucd;

CREATE TABLE ucd.codepoints (
    cp INT PRIMARY KEY,
    name TEXT,
    name1 TEXT,
    gc VARCHAR(10),
    ccc SMALLINT DEFAULT 0,
    sc VARCHAR(50),
    scx TEXT,
    blk VARCHAR(100),
    age VARCHAR(20),
    dt VARCHAR(20),
    dm TEXT,
    uc TEXT, lc TEXT, tc TEXT,
    suc TEXT, slc TEXT, stc TEXT,
    scf TEXT, cf TEXT,
    nt VARCHAR(20), nv TEXT,
    bc VARCHAR(10), bidi_m BOOLEAN, bmg TEXT, bidi_c BOOLEAN,
    bpt VARCHAR(5), bpb TEXT,
    jt VARCHAR(5), jg VARCHAR(50), join_c BOOLEAN,
    ea VARCHAR(5), lb VARCHAR(10),
    wb VARCHAR(10), sb VARCHAR(10), gcb VARCHAR(10),
    insc VARCHAR(30), inpc VARCHAR(30), vo VARCHAR(5),
    hst VARCHAR(10), jsn VARCHAR(10),
    alpha BOOLEAN, upper BOOLEAN, lower BOOLEAN, cased BOOLEAN,
    math BOOLEAN, hex BOOLEAN, ahex BOOLEAN,
    ideo BOOLEAN, uideo BOOLEAN, radical BOOLEAN,
    dash BOOLEAN, wspace BOOLEAN, qmark BOOLEAN,
    term BOOLEAN, sterm BOOLEAN, dia BOOLEAN, ext BOOLEAN,
    sd BOOLEAN, dep BOOLEAN, di BOOLEAN, vs BOOLEAN, nchar BOOLEAN,
    pat_ws BOOLEAN, pat_syn BOOLEAN,
    gr_base BOOLEAN, gr_ext BOOLEAN,
    ids BOOLEAN, idc BOOLEAN, xids BOOLEAN, xidc BOOLEAN,
    ce BOOLEAN, comp_ex BOOLEAN,
    cwl BOOLEAN, cwu BOOLEAN, cwt BOOLEAN, cwcf BOOLEAN, cwcm BOOLEAN, cwkcf BOOLEAN,
    emoji BOOLEAN, epres BOOLEAN, emod BOOLEAN, ebase BOOLEAN, ecomp BOOLEAN, extpict BOOLEAN,
    pcm BOOLEAN, ri BOOLEAN,
    nfc_qc VARCHAR(5), nfd_qc VARCHAR(5), nfkc_qc VARCHAR(5), nfkd_qc VARCHAR(5),
    nfkc_cf TEXT, nfkc_scf TEXT,
    han_radical SMALLINT, han_strokes SMALLINT,
    extra JSONB
);

CREATE TABLE ucd.name_aliases (
    cp INT REFERENCES ucd.codepoints(cp),
    alias TEXT NOT NULL,
    type VARCHAR(20) NOT NULL,
    PRIMARY KEY (cp, alias, type)
);

CREATE TABLE ucd.uca_weights (
    cp INT REFERENCES ucd.codepoints(cp),
    idx SMALLINT NOT NULL,
    primary_w INT, secondary_w INT, tertiary_w INT, quaternary_w INT,
    PRIMARY KEY (cp, idx)
);

CREATE TABLE ucd.confusables (
    source_cp INT,
    target_cp INT,
    PRIMARY KEY (source_cp, target_cp)
);

CREATE TABLE ucd.unihan (
    cp INT REFERENCES ucd.codepoints(cp),
    field VARCHAR(50) NOT NULL,
    value TEXT NOT NULL,
    PRIMARY KEY (cp, field)
);

CREATE INDEX idx_ucd_gc ON ucd.codepoints(gc);
CREATE INDEX idx_ucd_sc ON ucd.codepoints(sc);
CREATE INDEX idx_ucd_blk ON ucd.codepoints(blk);
CREATE INDEX idx_ucd_age ON ucd.codepoints(age);
