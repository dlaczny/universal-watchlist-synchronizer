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
        assertEquals(5, WatchlistConfig.clampGridColumns(1));
        assertEquals(5, WatchlistConfig.clampGridColumns(4));
        assertEquals(5, WatchlistConfig.clampGridColumns(5));
        assertEquals(7, WatchlistConfig.clampGridColumns(7));
        assertEquals(9, WatchlistConfig.clampGridColumns(9));
        assertEquals(9, WatchlistConfig.clampGridColumns(14));
    }

    @Test
    public void effectiveGridColumns_usesPreferredColumnsWhenTheyFit() {
        int columns = WatchlistConfig.effectiveGridColumns(7, 980, 138);

        assertEquals(7, columns);
    }

    @Test
    public void effectiveGridColumns_reducesColumnsWhenPreferredWouldOverflow() {
        int columns = WatchlistConfig.effectiveGridColumns(7, 700, 138);

        assertEquals(5, columns);
    }

    @Test
    public void effectiveGridColumns_keepsAtLeastOneColumnWhenViewportIsVeryNarrow() {
        int columns = WatchlistConfig.effectiveGridColumns(5, 100, 138);

        assertEquals(1, columns);
    }
}
