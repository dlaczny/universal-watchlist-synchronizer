package com.watchlist.tv;

import static org.junit.Assert.assertEquals;

import org.junit.Test;

public class WatchlistConfigTest {
    @Test
    public void normalizeBaseUrl_removesTrailingSlash() {
        assertEquals("http://10.0.2.2:5000", WatchlistConfig.normalizeBaseUrl("http://10.0.2.2:5000/"));
    }

    @Test
    public void clampGridColumns_keepsValuesInsideSupportedRange() {
        assertEquals(4, WatchlistConfig.clampGridColumns(1));
        assertEquals(4, WatchlistConfig.clampGridColumns(4));
        assertEquals(7, WatchlistConfig.clampGridColumns(7));
        assertEquals(9, WatchlistConfig.clampGridColumns(9));
        assertEquals(9, WatchlistConfig.clampGridColumns(14));
    }
}
