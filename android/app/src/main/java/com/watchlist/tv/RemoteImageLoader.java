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
import java.util.concurrent.RejectedExecutionException;

public final class RemoteImageLoader {
    public interface ActiveCheck {
        boolean isActive();
    }

    private final ExecutorService executor;
    private final ActiveCheck activeCheck;
    private final Handler mainHandler = new Handler(Looper.getMainLooper());

    public RemoteImageLoader(ExecutorService executor, ActiveCheck activeCheck) {
        this.executor = executor;
        this.activeCheck = activeCheck;
    }

    public void load(ImageView imageView, String imageUrl, int placeholderColor) {
        imageView.setBackgroundColor(placeholderColor);
        imageView.setImageBitmap(null);

        if (imageUrl == null || imageUrl.isEmpty()) {
            return;
        }

        if (!activeCheck.isActive()) {
            return;
        }

        try {
            executor.execute(() -> {
                try {
                    URL url = new URL(imageUrl);
                    HttpURLConnection connection = (HttpURLConnection) url.openConnection();
                    connection.setConnectTimeout(5000);
                    connection.setReadTimeout(5000);
                    try (InputStream stream = connection.getInputStream()) {
                        Bitmap bitmap = BitmapFactory.decodeStream(stream);
                        postIfActive(() -> imageView.setImageBitmap(bitmap));
                    } finally {
                        connection.disconnect();
                    }
                } catch (Exception ignored) {
                    postIfActive(() -> imageView.setBackgroundColor(Color.rgb(42, 48, 56)));
                }
            });
        } catch (RejectedExecutionException ignored) {
            // The owning Activity is tearing down.
        }
    }

    private void postIfActive(Runnable callback) {
        if (activeCheck.isActive()) {
            mainHandler.post(() -> {
                if (activeCheck.isActive()) {
                    callback.run();
                }
            });
        }
    }
}
