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
    private String issuer="Emisor del certificado";
    private final SignatureDrawing signature;
    private int pageWidth=600,pageHeight=800;
    private final ScaleGestureDetector scale;
    public PdfSelectionView(Context context) {
        super(context);signature=new SignatureDrawing(context); setContentDescription("Página PDF. Arrastra para colocar la firma; pellizca para ampliar.");
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
    public void setBitmap(Bitmap image,int width,int height) {pageWidth=width;pageHeight=height;Bitmap previous=bitmap; bitmap=image; box=null; resetZoom(); if(previous!=null&&previous!=image)previous.recycle(); }
    public void release() { if(bitmap!=null)bitmap.recycle(); bitmap=null;signature.release(); }
    public void setAppearance(es.firmavisible.core.SignatureAppearance value){signature.setStyle(value);invalidate();}
    public void setSigner(String name) { signer=name; invalidate(); }
    public void setSigner(String name,String issuer) {this.issuer=issuer;setSigner(name);}
    public float[] selection() { return box==null?null:box.clone(); }
    public void resetBox() { if(bitmap!=null) { box=new float[]{.1f,.72f,.55f,.14f}; invalidate(); } }
    public void resetZoom() { zoom=1;offsetX=offsetY=0;invalidate(); }
    private RectF pageRect() {
        float fit=Math.min((getWidth()-24f)/bitmap.getWidth(),(getHeight()-24f)/bitmap.getHeight());
        float w=bitmap.getWidth()*fit*zoom,h=bitmap.getHeight()*fit*zoom;
        return new RectF((getWidth()-w)/2+offsetX,(getHeight()-h)/2+offsetY,(getWidth()+w)/2+offsetX,(getHeight()+h)/2+offsetY);
    }
    protected void onDraw(Canvas canvas) {
        super.onDraw(canvas);canvas.drawColor(0xFFE8EDF5);
        if(bitmap==null) {
            float d=getResources().getDisplayMetrics().density,x=getWidth()/2f,y=getHeight()/2f;
            paint.setStyle(Paint.Style.STROKE);paint.setStrokeWidth(2*d);paint.setColor(0xFFAAB8D1);canvas.drawRoundRect(x-22*d,y-66*d,x+22*d,y-12*d,6*d,6*d,paint);
            canvas.drawLine(x-11*d,y-48*d,x+11*d,y-48*d,paint);canvas.drawLine(x-11*d,y-37*d,x+6*d,y-37*d,paint);
            paint.setStyle(Paint.Style.FILL);paint.setColor(0xFF344968);paint.setTextAlign(Paint.Align.CENTER);paint.setTypeface(Typeface.create("sans-serif-medium",Typeface.NORMAL));paint.setTextSize(17*getResources().getDisplayMetrics().scaledDensity);canvas.drawText("Aquí empieza tu firma",x,y+18*d,paint);
            paint.setTypeface(Typeface.DEFAULT);paint.setTextSize(12*getResources().getDisplayMetrics().scaledDensity);paint.setColor(0xFF66758E);canvas.drawText("Abre un PDF y elige dónde colocarla",x,y+42*d,paint);paint.setTextAlign(Paint.Align.LEFT);return;
        }
        RectF page=pageRect();canvas.drawBitmap(bitmap,null,page,paint);
        if(box!=null) {
            RectF r=new RectF(page.left+box[0]*page.width(),page.top+box[1]*page.height(),page.left+(box[0]+box[2])*page.width(),page.top+(box[1]+box[3])*page.height());
            signature.draw(canvas,r,box[2]*pageWidth,box[3]*pageHeight,signer,issuer);
            paint.setColor(0xFF3155D9);paint.setStyle(Paint.Style.STROKE);paint.setStrokeWidth(1.2f*getResources().getDisplayMetrics().density);canvas.drawRect(r,paint);paint.setStyle(Paint.Style.FILL);
            canvas.drawCircle(r.right,r.bottom,4*getResources().getDisplayMetrics().density,paint);
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
