package com.watchlist.tv;

public final class SyncStatus {
    private final String status;
    private final String lastSuccessfulSyncAt;

    public SyncStatus(String status, String lastSuccessfulSyncAt) {
        this.status = status;
        this.lastSuccessfulSyncAt = lastSuccessfulSyncAt;
    }

    public String status() {
        return status;
    }

    public String lastSuccessfulSyncAt() {
        return lastSuccessfulSyncAt;
    }
}
