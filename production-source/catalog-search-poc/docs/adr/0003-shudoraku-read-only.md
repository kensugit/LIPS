# ADR 0003: 酒道楽 is a read-only source system

Status: Accepted

酒道楽を参照専用Source Systemとして扱い、Snapshot/差分同期する。直接joinや書戻しを行わない。検索Indexは酒道楽固有の名称・カナ・JAN・備考を含み、未Link商品も検索可能にする。
