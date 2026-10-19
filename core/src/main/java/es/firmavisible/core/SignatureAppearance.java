package es.firmavisible.core;

import java.time.*;
import java.time.format.DateTimeFormatter;

/** Immutable appearance; no certificate or private key is persisted with it. */
public final class SignatureAppearance {
    public static final String DEFAULT_TEXT = "Firmado digitalmente\n{nombre}\n{fecha}";
    public final String template;
    public final boolean decorated;
    public final float textSize;
    private final byte[] image;
    public SignatureAppearance(String text, byte[] png, boolean decorated) {
        this(text,png,decorated,8);
    }
    public SignatureAppearance(String text, byte[] png, boolean decorated, float textSize) {
        if(!Float.isFinite(textSize)||textSize<4||textSize>18)throw new IllegalArgumentException("El tamaño del texto debe estar entre 4 y 18 pt.");
        if (text == null || text.length() > 600) throw new IllegalArgumentException("El texto admite hasta 600 caracteres.");
        if (png != null && png.length > 4*1024*1024) throw new IllegalArgumentException("La imagen es demasiado grande.");
        if (text.trim().isEmpty() && (png == null || png.length == 0)) throw new IllegalArgumentException("Añade un texto o una imagen a la firma.");
        this.template=text; this.image=png==null?null:png.clone(); this.decorated=decorated;this.textSize=textSize;
    }
    public static SignatureAppearance standard() { return new SignatureAppearance(DEFAULT_TEXT,null,false); }
    public byte[] image() { return image==null?null:image.clone(); }
    public String text(String name, String issuer, Instant time) {
        String date=DateTimeFormatter.ofPattern("dd/MM/yyyy HH:mm:ss XXX").withZone(ZoneId.systemDefault()).format(time);
        return template.replace("{nombre}",name).replace("{emisor}",issuer).replace("{fecha}",date).replace("\r", "");
    }
}
