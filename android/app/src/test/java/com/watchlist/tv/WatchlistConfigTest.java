package com.watchlist.tv;

import static org.junit.Assert.assertEquals;

import org.junit.Test;

public class WatchlistConfigTest {
    @Test
    public void normalizeBaseUrl_removesTrailingSlash() {
        assertEquals("http://10.0.2.2:5000", WatchlistConfig.normalizeBaseUrl("http://10.0.2.2:5000/"));
    }
}
