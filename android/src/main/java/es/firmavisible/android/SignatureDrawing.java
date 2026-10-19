package es.firmavisible.android;

import android.content.Context;
import android.graphics.*;
import es.firmavisible.core.*;
import java.io.*;
import java.time.Instant;

/** Same layout/font as the signed PDF, rendered at the selected visual dimensions. */
final class SignatureDrawing {
    private final Paint paint=new Paint(Paint.ANTI_ALIAS_FLAG|Paint.FILTER_BITMAP_FLAG);
    private final Typeface font;
    private Bitmap image;
    private SignatureAppearance style=SignatureAppearance.standard();
    SignatureDrawing(Context context) { font=Typeface.createFromAsset(context.getAssets(),"fonts/NotoSans-Regular.ttf"); }
    void setStyle(SignatureAppearance value) {
        if(image!=null)image.recycle();style=value;byte[] bytes=value.image();image=bytes==null?null:BitmapFactory.decodeByteArray(bytes,0,bytes.length);
    }
    void release(){if(image!=null)image.recycle();image=null;}
    void draw(Canvas canvas,RectF target,float width,float height,String name,String issuer) {
        canvas.save();canvas.clipRect(target);canvas.translate(target.left,target.top);canvas.scale(target.width()/width,target.height()/height);
        paint.setStyle(Paint.Style.FILL);
        if(style.decorated){paint.setColor(Color.rgb(41,89,166));paint.setStyle(Paint.Style.STROKE);paint.setStrokeWidth(.7f);canvas.drawRect(.5f,.5f,width-.5f,height-.5f,paint);paint.setStyle(Paint.Style.FILL);}
        paint.setTypeface(font);paint.setTextSize(100);
        try {
            AppearanceLayout layout=AppearanceLayout.fit(width,height,style.text(name,issuer,Instant.now()),image!=null,style.textSize,text->paint.measureText(text)/100.0);
            if(image!=null){float scale=Math.min(layout.imageWidth/image.getWidth(),layout.imageHeight/image.getHeight());float w=image.getWidth()*scale,h=image.getHeight()*scale;
                float left=layout.imageX+(layout.imageWidth-w)/2,top=6+(layout.imageHeight-h)/2;
                canvas.drawBitmap(image,null,new RectF(left,top,left+w,top+h),paint);}
            paint.setColor(Color.rgb(20,41,71));paint.setTextSize(layout.size);
            float y=6+layout.size;for(String line:layout.lines){canvas.drawText(line,layout.textX,y,paint);y+=layout.size*1.3f;}
        } catch(IllegalArgumentException e){paint.setTextSize(7);paint.setColor(Color.rgb(170,55,40));canvas.drawText("Amplía el recuadro o acorta el texto",6,16,paint);}
        canvas.restore();
    }
}
