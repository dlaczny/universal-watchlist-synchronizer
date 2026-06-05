package com.watchlist.tv;

public record AvailabilityRefreshResult(
        String status,
        boolean ranPlexSync,
        String reason) {
}
