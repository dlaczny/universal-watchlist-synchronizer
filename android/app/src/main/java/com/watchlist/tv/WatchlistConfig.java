package com.watchlist.tv;

public final class WatchlistConfig {
    private static final int MIN_GRID_COLUMNS = 4;
    private static final int MAX_GRID_COLUMNS = 9;

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

    public static int gridColumns() {
        return clampGridColumns(BuildConfig.WATCHLIST_GRID_COLUMNS);
    }

    static int clampGridColumns(int value) {
        if (value < MIN_GRID_COLUMNS) {
            return MIN_GRID_COLUMNS;
        }
        if (value > MAX_GRID_COLUMNS) {
            return MAX_GRID_COLUMNS;
        }
        return value;
    }
}
