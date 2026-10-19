package es.firmavisible.android;

import android.app.*;
import android.graphics.*;
import android.graphics.drawable.GradientDrawable;
import android.text.*;
import android.view.*;
import android.widget.*;
import es.firmavisible.core.SignatureAppearance;
import java.util.function.Consumer;

final class AppearanceEditor {
    private final Activity activity;
    private final Dialog dialog;
    private final EditText text;
    private final Switch decorated;
    private final SeekBar textSize;
    private final TextView sizeLabel;
    private byte[] image;
    private final View sample;
    private final SignatureDrawing drawing;
    private final TextView imageState;
    AppearanceEditor(Activity owner,SignatureAppearance initial,String name,String issuer,Runnable pickImage,Consumer<SignatureAppearance> save) {
        activity=owner;image=initial.image();drawing=new SignatureDrawing(owner);dialog=new Dialog(owner);dialog.setTitle("Tu firma visible");
        LinearLayout content=new LinearLayout(owner);content.setOrientation(LinearLayout.VERTICAL);content.setPadding(dp(22),dp(22),dp(22),dp(24));content.setBackgroundColor(0xFFF5F7FC);
        TextView title=label("Tu firma visible",25);title.setTypeface(null,Typeface.BOLD);content.addView(title);
        content.addView(label("Diseña una apariencia que puedas reutilizar.",13));
        sample=new View(owner){protected void onDraw(Canvas canvas){super.onDraw(canvas);canvas.drawColor(Color.WHITE);float width=getWidth()-dp(16),height=width*104/320;float top=(getHeight()-height)/2;drawing.draw(canvas,new RectF(dp(8),top,getWidth()-dp(8),top+height),320,104,name,issuer);}};
        LinearLayout.LayoutParams sampleParams=new LinearLayout.LayoutParams(-1,dp(112));sampleParams.topMargin=dp(18);sampleParams.bottomMargin=dp(12);content.addView(sample,sampleParams);
        content.addView(label("VISTA PREVIA · se adapta al recuadro del PDF",10));
        sizeLabel=label("",14);content.addView(sizeLabel);
        textSize=new SeekBar(owner);textSize.setMax(28);textSize.setProgress(Math.round((initial.textSize-4)*2));textSize.setTag("appearance-text-size");textSize.setContentDescription("Tamaño del texto de la firma");content.addView(textSize);
        content.addView(label("De 4 a 18 pt. Se reduce si hace falta para caber. El tamaño se aplica al PDF y se guarda con tu diseño.",11));
        content.addView(label("Texto de la firma",15));text=new EditText(owner);text.setTextSize(15);text.setMinLines(3);text.setGravity(Gravity.TOP);text.setText(initial.template);text.setFilters(new InputFilter[]{new InputFilter.LengthFilter(600)});text.setPadding(dp(12),dp(10),dp(12),dp(10));text.setBackground(background(Color.WHITE));content.addView(text,new LinearLayout.LayoutParams(-1,-2));
        LinearLayout tokens=new LinearLayout(owner);button(tokens,"+ Nombre",()->insert("{nombre}"));button(tokens,"+ Fecha",()->insert("{fecha}"));button(tokens,"+ Emisor",()->insert("{emisor}"));content.addView(tokens);
        content.addView(label("La fecha visible es la del móvil. El sello acreditado se consulta en el panel de firmas del PDF.",11));
        imageState=label("",12);content.addView(imageState);
        LinearLayout images=new LinearLayout(owner);button(images,"Elegir imagen",pickImage);button(images,"Quitar imagen",()->setImage(null));content.addView(images);
        content.addView(label("PNG o JPEG. Se conserva su proporción. Puedes dejar el texto vacío para utilizar solo la imagen.",11));
        decorated=new Switch(owner);decorated.setText("Mostrar marco (fondo transparente)");decorated.setTextColor(0xFF132A44);decorated.setChecked(initial.decorated);decorated.setPadding(0,dp(14),0,dp(14));content.addView(decorated);
        content.addView(label("El fondo de la firma es transparente. Si tu imagen tiene fondo blanco, utiliza un PNG con transparencia para quitarlo.",11));
        content.addView(label("El texto y la imagen se guardan en este móvil. No se guardan con ellos el certificado ni su contraseña.",11));
        LinearLayout actions=new LinearLayout(owner);button(actions,"Cancelar",dialog::dismiss);Button done=button(actions,"Guardar diseño",()->{try{save.accept(current());}catch(IllegalArgumentException e){Toast.makeText(owner,e.getMessage(),Toast.LENGTH_LONG).show();}});done.setTextColor(Color.WHITE);done.setBackground(background(0xFF3155D9));content.addView(actions);
        ScrollView scroll=new ScrollView(owner);scroll.setFillViewport(true);scroll.addView(content);scroll.setOnApplyWindowInsetsListener((v,insets)->{v.setPadding(insets.getSystemWindowInsetLeft(),insets.getSystemWindowInsetTop(),insets.getSystemWindowInsetRight(),insets.getSystemWindowInsetBottom());return insets;});dialog.setContentView(scroll);
        text.setTag("appearance-text");done.setTag("save-design");
        text.addTextChangedListener(new TextWatcher(){public void beforeTextChanged(CharSequence s,int st,int c,int a){}public void onTextChanged(CharSequence s,int st,int b,int c){update();}public void afterTextChanged(Editable e){}});
        decorated.setOnCheckedChangeListener((b,c)->update());dialog.setOnDismissListener(d->drawing.release());update();
        textSize.setOnSeekBarChangeListener(new SeekBar.OnSeekBarChangeListener(){public void onProgressChanged(SeekBar bar,int value,boolean user){update();}public void onStartTrackingTouch(SeekBar bar){}public void onStopTrackingTouch(SeekBar bar){}});
    }
    private int dp(int value){return Math.round(value*activity.getResources().getDisplayMetrics().density);}
    private TextView label(String value,int size){TextView v=new TextView(activity);v.setText(value);v.setTextSize(size);v.setTextColor(0xFF273D58);v.setPadding(0,dp(4),0,dp(6));return v;}
    private GradientDrawable background(int color){GradientDrawable shape=new GradientDrawable();shape.setColor(color);shape.setCornerRadius(dp(10));return shape;}
    private Button button(LinearLayout row,String value,Runnable run){Button button=new Button(activity);button.setText(value);button.setTextSize(12);button.setAllCaps(false);button.setOnClickListener(v->run.run());LinearLayout.LayoutParams p=new LinearLayout.LayoutParams(0,dp(48),1);p.setMargins(dp(2),dp(4),dp(2),dp(4));row.addView(button,p);return button;}
    private SignatureAppearance current(){return new SignatureAppearance(text.getText().toString(),image,decorated.isChecked(),4+textSize.getProgress()*.5f);}
    private void insert(String token){int start=Math.max(0,text.getSelectionStart()),end=Math.max(start,text.getSelectionEnd());text.getText().replace(start,end,token);}
    private void update(){sizeLabel.setText("Tamaño del texto · "+(4+textSize.getProgress()*.5f)+" pt");imageState.setText(image==null?"Imagen · sin imagen":"Imagen · guardada en este móvil");try{drawing.setStyle(current());sample.invalidate();}catch(IllegalArgumentException ignored){}}
    void setImage(byte[] png){image=png;update();}
    void show(){dialog.show();if(dialog.getWindow()!=null){dialog.getWindow().setBackgroundDrawableResource(android.R.color.transparent);dialog.getWindow().setLayout(-1,-1);}}
    boolean isShowing(){return dialog.isShowing();}
    void setBusy(boolean value){dialog.setCancelable(!value);if(dialog.getWindow()!=null){if(value)dialog.getWindow().addFlags(WindowManager.LayoutParams.FLAG_NOT_TOUCHABLE);else dialog.getWindow().clearFlags(WindowManager.LayoutParams.FLAG_NOT_TOUCHABLE);}}
    void dismiss(){dialog.dismiss();}
}
