package com.watchlist.tv;

public final class WatchlistConfig {
    private WatchlistConfig() {
    }

    public static String apiBaseUrl() {
        return normalizeBaseUrl(BuildConfig.WATCHLIST_API_BASE_URL);
    }

    public static String normalizeBaseUrl(String value) {
        if (value.endsWith("/")) {
            return value.substring(0, value.length() - 1);
        }

        return value;
    }
}
