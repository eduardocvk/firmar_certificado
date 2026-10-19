package es.firmavisible.core;

/** Rectangle in normalized displayed page coordinates, origin at top left. */
public final class Placement {
    public final int page;
    public final float x, y, width, height;
    public Placement(int page, float x, float y, float width, float height) {
        if (page < 0 || !Float.isFinite(x) || !Float.isFinite(y) || !Float.isFinite(width) || !Float.isFinite(height)
            || x < 0 || y < 0 || width <= 0 || height <= 0 || x + width > 1.00001f || y + height > 1.00001f)
            throw new IllegalArgumentException("Selecciona un recuadro dentro de la página.");
        this.page = page; this.x = x; this.y = y; this.width = width; this.height = height;
    }
    /** x,y,width,height in unrotated PDF space, including nonzero CropBox origin. */
    public float[] pdfRectangle(float left, float bottom, float w, float h, int rotation) {
        switch (Math.floorMod(rotation, 360)) {
            case 0: return new float[]{left+x*w, bottom+(1-y-height)*h, width*w, height*h};
            case 90: return new float[]{left+y*w, bottom+x*h, height*w, width*h};
            case 180: return new float[]{left+(1-x-width)*w, bottom+y*h, width*w, height*h};
            case 270: return new float[]{left+(1-y-height)*w, bottom+(1-x-width)*h, height*w, width*h};
            default: throw new IllegalArgumentException("Rotación de página no admitida.");
        }
    }
}
