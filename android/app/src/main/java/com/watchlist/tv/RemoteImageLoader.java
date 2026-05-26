package com.watchlist.tv;

import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.graphics.Color;
import android.os.Handler;
import android.os.Looper;
import android.widget.ImageView;
import java.io.InputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.util.concurrent.ExecutorService;

public final class RemoteImageLoader {
    private final ExecutorService executor;
    private final Handler mainHandler = new Handler(Looper.getMainLooper());

    public RemoteImageLoader(ExecutorService executor) {
        this.executor = executor;
    }

    public void load(ImageView imageView, String imageUrl, int placeholderColor) {
        imageView.setBackgroundColor(placeholderColor);
        imageView.setImageBitmap(null);

        if (imageUrl == null || imageUrl.isEmpty()) {
            return;
        }

        executor.execute(() -> {
            try {
                URL url = new URL(imageUrl);
                HttpURLConnection connection = (HttpURLConnection) url.openConnection();
                connection.setConnectTimeout(5000);
                connection.setReadTimeout(5000);
                try (InputStream stream = connection.getInputStream()) {
                    Bitmap bitmap = BitmapFactory.decodeStream(stream);
                    mainHandler.post(() -> imageView.setImageBitmap(bitmap));
                } finally {
                    connection.disconnect();
                }
            } catch (Exception ignored) {
                mainHandler.post(() -> imageView.setBackgroundColor(Color.rgb(42, 48, 56)));
            }
        });
    }
}
