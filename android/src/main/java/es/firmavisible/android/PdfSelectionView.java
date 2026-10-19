package es.firmavisible.android;

import android.content.Context;
import android.graphics.*;
import android.view.*;

/** Selection in page coordinates, independent of fit, pinch zoom and two-finger pan. */
public final class PdfSelectionView extends View {
    private Bitmap bitmap;
    private final Paint paint=new Paint(Paint.ANTI_ALIAS_FLAG);
    private float[] box, before;
    private float zoom=1, offsetX, offsetY, startX, startY, panX, panY;
    private int mode;
    private boolean drawing;
    private String signer="Selecciona un certificado";
    private final ScaleGestureDetector scale;
    public PdfSelectionView(Context context) {
        super(context); setContentDescription("Página PDF. Arrastra para colocar la firma; pellizca para ampliar.");
        scale=new ScaleGestureDetector(context,new ScaleGestureDetector.SimpleOnScaleGestureListener() {
            public boolean onScaleBegin(ScaleGestureDetector detector) { drawing=false; box=before==null?null:before.clone(); return true; }
            public boolean onScale(ScaleGestureDetector detector) {
                float old=zoom; zoom=Math.max(1,Math.min(5,zoom*detector.getScaleFactor()));
                float ratio=zoom/old;
                float focusX=detector.getFocusX()-getWidth()/2f,focusY=detector.getFocusY()-getHeight()/2f;
                offsetX=focusX-(focusX-offsetX)*ratio;
                offsetY=focusY-(focusY-offsetY)*ratio;
                invalidate(); return true;
            }
        });
    }
    public void setBitmap(Bitmap image) { Bitmap previous=bitmap; bitmap=image; box=null; resetZoom(); if(previous!=null&&previous!=image)previous.recycle(); }
    public void release() { if(bitmap!=null)bitmap.recycle(); bitmap=null; }
    public void setSigner(String name) { signer=name; invalidate(); }
    public float[] selection() { return box==null?null:box.clone(); }
    public void resetBox() { if(bitmap!=null) { box=new float[]{.1f,.72f,.55f,.14f}; invalidate(); } }
    public void resetZoom() { zoom=1;offsetX=offsetY=0;invalidate(); }
    private RectF pageRect() {
        float fit=Math.min((getWidth()-24f)/bitmap.getWidth(),(getHeight()-24f)/bitmap.getHeight());
        float w=bitmap.getWidth()*fit*zoom,h=bitmap.getHeight()*fit*zoom;
        return new RectF((getWidth()-w)/2+offsetX,(getHeight()-h)/2+offsetY,(getWidth()+w)/2+offsetX,(getHeight()+h)/2+offsetY);
    }
    protected void onDraw(Canvas canvas) {
        super.onDraw(canvas);canvas.drawColor(Color.rgb(225,232,240));
        if(bitmap==null) { paint.setColor(Color.DKGRAY);paint.setTextSize(16*getResources().getDisplayMetrics().scaledDensity);canvas.drawText("Abre un PDF para empezar",24,getHeight()/2f,paint);return; }
        RectF page=pageRect();canvas.drawBitmap(bitmap,null,page,paint);
        if(box!=null) {
            RectF r=new RectF(page.left+box[0]*page.width(),page.top+box[1]*page.height(),page.left+(box[0]+box[2])*page.width(),page.top+(box[1]+box[3])*page.height());
            paint.setStyle(Paint.Style.FILL);paint.setColor(Color.argb(240,231,240,255));canvas.drawRect(r,paint);
            paint.setColor(Color.rgb(28,75,140));paint.setStyle(Paint.Style.STROKE);paint.setStrokeWidth(3);canvas.drawRect(r,paint);paint.setStyle(Paint.Style.FILL);
            canvas.save();canvas.clipRect(r);float size=Math.min(22,Math.max(10,r.height()/5));paint.setTextSize(size);
            canvas.drawText("Firmado digitalmente",r.left+6,r.top+size+4,paint);canvas.drawText(signer,r.left+6,r.top+2.2f*size+4,paint);
            canvas.drawText("Fecha y hora del dispositivo",r.left+6,r.top+3.4f*size+4,paint);canvas.restore();
            canvas.drawRect(r.right-10,r.bottom-10,r.right+10,r.bottom+10,paint);
        }
    }
    private float clamp(float value) { return Math.max(0,Math.min(1,value)); }
    public boolean onTouchEvent(MotionEvent e) {
        if(bitmap==null||!isEnabled())return false;
        scale.onTouchEvent(e);
        if(e.getPointerCount()>1) {
            float cx=(e.getX(0)+e.getX(1))/2,cy=(e.getY(0)+e.getY(1))/2;
            if(e.getActionMasked()==MotionEvent.ACTION_MOVE) { offsetX+=cx-panX;offsetY+=cy-panY;invalidate(); }
            panX=cx;panY=cy;drawing=false;return true;
        }
        RectF p=pageRect();float x=clamp((e.getX()-p.left)/p.width()),y=clamp((e.getY()-p.top)/p.height());
        switch(e.getActionMasked()) {
            case MotionEvent.ACTION_DOWN:
                if(!p.contains(e.getX(),e.getY()))return false;
                before=box==null?null:box.clone();startX=x;startY=y;drawing=true;mode=0;
                if(box!=null&&Math.abs(x-box[0]-box[2])*p.width()<30&&Math.abs(y-box[1]-box[3])*p.height()<30)mode=2;
                else if(box!=null&&x>=box[0]&&x<=box[0]+box[2]&&y>=box[1]&&y<=box[1]+box[3])mode=1;
                return true;
            case MotionEvent.ACTION_MOVE:
                if(!drawing||scale.isInProgress())return true;
                if(mode==1)box=new float[]{Math.max(0,Math.min(1-before[2],before[0]+x-startX)),Math.max(0,Math.min(1-before[3],before[1]+y-startY)),before[2],before[3]};
                else if(mode==2)box=new float[]{before[0],before[1],Math.max(.001f,x-before[0]),Math.max(.001f,y-before[1])};
                else box=new float[]{Math.min(startX,x),Math.min(startY,y),Math.max(.001f,Math.abs(x-startX)),Math.max(.001f,Math.abs(y-startY))};
                invalidate();return true;
            case MotionEvent.ACTION_UP: drawing=false;performClick();return true;
            case MotionEvent.ACTION_CANCEL: drawing=false;box=before;invalidate();return true;
            default:return true;
        }
    }
    @Override public boolean performClick() { super.performClick();return true; }
}
