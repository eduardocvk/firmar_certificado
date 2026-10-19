package es.firmavisible.core;

import java.util.*;
import java.util.function.ToDoubleFunction;

/** Layout in visual PDF points, shared by the PDF writer and Android preview. */
public final class AppearanceLayout {
    public final float size,textX,imageX=6,imageY=6,imageWidth,imageHeight;
    public final List<String> lines;
    private AppearanceLayout(float size,float x,float iw,float ih,List<String> lines) {
        this.size=size;textX=x;imageWidth=iw;imageHeight=ih;this.lines=lines;
    }
    public static AppearanceLayout fit(float width,float height,String text,boolean image,ToDoubleFunction<String> measure) {
        return fit(width,height,text,image,8,measure);
    }
    public static AppearanceLayout fit(float width,float height,String text,boolean image,float textSize,ToDoubleFunction<String> measure) {
        if(!Float.isFinite(textSize)||textSize<4||textSize>18)throw new IllegalArgumentException("El tamaño del texto debe estar entre 4 y 18 pt.");
        float iw=image?(text.trim().isEmpty()?width-12:(width-18)*.5f):0;
        float x=image&&!text.trim().isEmpty()?iw+12:6;
        if(text.trim().isEmpty())return new AppearanceLayout(10,x,iw,height-12,Collections.emptyList());
        float available=width-x-6;
        for(float size=textSize;size>=4;size-=.25f) {
            List<String> lines=new ArrayList<>();boolean fits=true;
            for(String paragraph:text.split("\n",-1)) {
                String line="";
                for(int index=0;index<paragraph.length();) {
                    int code=paragraph.codePointAt(index);index+=Character.charCount(code);
                    String next=line+new String(Character.toChars(code));
                    if(measure.applyAsDouble(next)*size>available) {
                        if(line.isEmpty()){fits=false;break;}
                        int space=line.lastIndexOf(' ');
                        if(space>0){lines.add(line.substring(0,space));line=line.substring(space+1)+new String(Character.toChars(code));}
                        else{lines.add(line);line=new String(Character.toChars(code));}
                    } else line=next;
                }
                lines.add(line);
            }
            if(fits&&lines.size()*size*1.3f<=height-12)return new AppearanceLayout(size,x,iw,height-12,lines);
        }
        throw new IllegalArgumentException("El texto no cabe de forma legible. Amplía el recuadro o acorta el texto.");
    }
}
