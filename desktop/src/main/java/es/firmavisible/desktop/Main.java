package es.firmavisible.desktop;

import es.firmavisible.core.*;
import es.firmavisible.pdf.PdfEngine;
import java.awt.*;
import java.awt.event.*;
import java.awt.image.BufferedImage;
import java.io.*;
import java.nio.file.*;
import java.security.KeyStore;
import java.util.*;
import java.util.List;
import java.util.concurrent.Callable;
import java.util.function.Consumer;
import javax.swing.*;
import javax.swing.filechooser.FileNameExtensionFilter;
import org.apache.pdfbox.pdmodel.PDDocument;
import org.apache.pdfbox.rendering.PDFRenderer;

public final class Main extends JFrame {
    private final JLabel status = new JLabel("Abre un PDF para empezar.");
    private final JLabel pageLabel = new JLabel("Sin documento", SwingConstants.CENTER);
    private final JComboBox<Identity> identities = new JComboBox<>();
    private final Preview preview = new Preview();
    private final JPanel controls = new JPanel();
    private File snapshot, original;
    private int page, pages;
    private boolean busy;
    private final List<File> tempFiles = new ArrayList<>();

    public static void main(String[] args) {
        SwingUtilities.invokeLater(() -> {
            try { UIManager.setLookAndFeel(UIManager.getSystemLookAndFeelClassName()); } catch (Exception ignored) {}
            new Main().setVisible(true);
        });
    }
    Main() {
        super("FirmaVisible · PDF con certificado");
        setDefaultCloseOperation(WindowConstants.DISPOSE_ON_CLOSE);
        setMinimumSize(new Dimension(900,650)); setSize(1200,850); setLocationRelativeTo(null);
        setLayout(new BorderLayout(16,16));
        controls.setLayout(new BoxLayout(controls,BoxLayout.Y_AXIS));
        controls.setBorder(BorderFactory.createEmptyBorder(24,20,24,20));
        controls.setPreferredSize(new Dimension(300,600));
        JLabel brand = new JLabel("FirmaVisible"); brand.setFont(new Font("Segoe UI",Font.BOLD,28)); controls.add(brand);
        label("PDF firmado, en tu dispositivo"); gap();
        label("1. DOCUMENTO"); button("Abrir PDF…", this::openPdf); gap();
        label("2. CERTIFICADO"); button("Certificados de Windows…", this::windowsCertificates);
        button("Importar .p12 / .pfx…", this::importCertificate);
        identities.setMaximumSize(new Dimension(Integer.MAX_VALUE,36)); controls.add(identities);
        identities.addActionListener(e -> preview.repaint());
        button("Olvidar certificado", () -> { identities.removeAllItems(); preview.repaint(); }); gap();
        label("3. APARIENCIA VISIBLE");
        JLabel instructions = new JLabel("<html>Arrastra sobre la página para dibujar<br>el recuadro. Arrastra su interior para<br>moverlo, o la esquina para cambiar<br>su tamaño.</html>"); controls.add(instructions);
        button("Restablecer recuadro", () -> { preview.box = new float[]{.1f,.72f,.5f,.13f}; preview.repaint(); }); gap();
        button("Firmar y guardar copia…", this::sign); button("Comprobar firmas del PDF", this::verify); gap();
        JLabel notice = new JLabel("<html><small>Firma local con certificado.<br>Sin sello de tiempo en esta versión.<br>La comprobación de integridad no<br>comprueba confianza ni revocación.</small></html>"); controls.add(notice);
        add(controls,BorderLayout.WEST);
        JPanel center = new JPanel(new BorderLayout());
        JPanel navigation = new JPanel();
        JButton prev = new JButton("← Anterior"), next = new JButton("Siguiente →");
        prev.addActionListener(e -> { if (!busy && page>0) { page--; render(); } });
        next.addActionListener(e -> { if (!busy && page+1<pages) { page++; render(); } });
        navigation.add(prev); navigation.add(pageLabel); navigation.add(next);
        center.add(navigation,BorderLayout.NORTH);
        JScrollPane scroll = new JScrollPane(preview); scroll.getVerticalScrollBar().setUnitIncrement(24);
        center.add(scroll,BorderLayout.CENTER); add(center,BorderLayout.CENTER);
        status.setBorder(BorderFactory.createEmptyBorder(10,20,14,20)); add(status,BorderLayout.SOUTH);
        addWindowListener(new WindowAdapter() { @Override public void windowClosed(WindowEvent e) {
            identities.removeAllItems(); for(File f:tempFiles) { try { Files.deleteIfExists(f.toPath()); } catch(IOException ignored) {} }
        }});
    }
    private void label(String text) { JLabel label=new JLabel(text); label.setBorder(BorderFactory.createEmptyBorder(8,0,8,0)); controls.add(label); }
    private void gap() { controls.add(Box.createVerticalStrut(14)); }
    private void button(String text, Runnable action) {
        JButton button=new JButton(text); button.setAlignmentX(Component.LEFT_ALIGNMENT);
        button.setMaximumSize(new Dimension(Integer.MAX_VALUE,38)); button.addActionListener(e -> { if(!busy) action.run(); }); controls.add(button);
        controls.add(Box.createVerticalStrut(6));
    }
    private File choose(String title, String... extensions) {
        JFileChooser chooser=new JFileChooser(); chooser.setDialogTitle(title);
        chooser.setFileFilter(new FileNameExtensionFilter(title,extensions));
        return chooser.showOpenDialog(this)==JFileChooser.APPROVE_OPTION ? chooser.getSelectedFile():null;
    }
    private <T> void work(String message, Callable<T> task, Consumer<T> success) {
        busy=true; preview.setEnabled(false); status.setText(message); setControlsEnabled(controls,false);
        new SwingWorker<T,Void>() {
            protected T doInBackground() throws Exception { return task.call(); }
            protected void done() {
                busy=false; preview.setEnabled(true); setControlsEnabled(controls,true);
                try { T value=get(); status.setText("Listo."); success.accept(value); }
                catch(Exception ex) { Throwable cause=ex.getCause()==null?ex:ex.getCause(); status.setText("Operación no completada.");
                    JOptionPane.showMessageDialog(Main.this,cause.getMessage(),"FirmaVisible",JOptionPane.ERROR_MESSAGE); }
            }
        }.execute();
    }
    private void setControlsEnabled(Container container, boolean enabled) {
        for(Component c:container.getComponents()) { c.setEnabled(enabled); if(c instanceof Container) setControlsEnabled((Container)c,enabled); }
    }
    private void openPdf() {
        File selected=choose("Documento PDF","pdf"); if(selected==null)return;
        work("Abriendo documento…", () -> {
            Path copy=Files.createTempFile("firmavisible-", ".pdf");
            try { Files.copy(selected.toPath(),copy,StandardCopyOption.REPLACE_EXISTING);
                try(PDDocument doc=PDDocument.load(copy.toFile())) { if(doc.getNumberOfPages()==0)throw new IOException("PDF sin páginas."); }
                return copy.toFile();
            } catch(Exception ex) { Files.deleteIfExists(copy); throw ex; }
        }, copy -> {
            if(snapshot!=null) { try { Files.deleteIfExists(snapshot.toPath()); } catch(IOException ignored) {} }
            snapshot=copy; original=selected; tempFiles.add(copy); page=0; preview.box=null; render();
        });
    }
    private void render() {
        File current=snapshot; int index=page;
        work("Preparando página…", () -> {
            try(PDDocument doc=PDDocument.load(current)) {
                pages=doc.getNumberOfPages();
                var crop=doc.getPage(index).getCropBox(); boolean rotated=Math.floorMod(doc.getPage(index).getRotation(),180)!=0;
                float width=rotated?crop.getHeight():crop.getWidth();
                float height=rotated?crop.getWidth():crop.getHeight();
                return new PDFRenderer(doc).renderImage(index,Math.min(1.8f,Math.min(1200/width,1600/height)));
            }
        }, image -> {
            preview.image=image; preview.box=null; preview.setPreferredSize(new Dimension(image.getWidth()+40,image.getHeight()+40));
            preview.revalidate(); preview.repaint(); pageLabel.setText("Página "+(page+1)+" de "+pages);
            status.setText("Dibuja el recuadro donde quieres firmar.");
        });
    }
    private void setIdentities(List<Identity> values) { identities.removeAllItems(); for(Identity i:values)identities.addItem(i); }
    private void windowsCertificates() {
        work("Leyendo certificados de Windows…", () -> { KeyStore store=KeyStore.getInstance("Windows-MY"); store.load(null,null); return Identity.fromStore(store,null); },this::setIdentities);
    }
    private void importCertificate() {
        File selected=choose("Certificado con clave privada","p12","pfx"); if(selected==null)return;
        JPasswordField passwordField=new JPasswordField();
        if(JOptionPane.showConfirmDialog(this,passwordField,"Contraseña del certificado",JOptionPane.OK_CANCEL_OPTION)!=JOptionPane.OK_OPTION)return;
        char[] password=passwordField.getPassword(); passwordField.setText("");
        work("Abriendo certificado…", () -> { try(InputStream in=new FileInputStream(selected)) { return Identity.fromPkcs12(in,password); } finally { Arrays.fill(password,'\0'); } },this::setIdentities);
    }
    private void sign() {
        Identity identity=(Identity)identities.getSelectedItem();
        if(snapshot==null || preview.box==null || identity==null) { JOptionPane.showMessageDialog(this,"Abre un PDF, selecciona un certificado y dibuja el recuadro.");return; }
        float[] b=preview.box; Placement placement;
        try { placement=new Placement(page,b[0],b[1],b[2],b[3]); }
        catch(IllegalArgumentException e) { JOptionPane.showMessageDialog(this,e.getMessage());return; }
        JFileChooser chooser=new JFileChooser(); chooser.setDialogTitle("Guardar PDF firmado");
        chooser.setSelectedFile(new File(original.getParentFile(),original.getName().replaceFirst("(?i)\\.pdf$","")+"_firmado.pdf"));
        if(chooser.showSaveDialog(this)!=JFileChooser.APPROVE_OPTION)return;
        File target=chooser.getSelectedFile();
        if(!target.getName().toLowerCase(Locale.ROOT).endsWith(".pdf"))target=new File(target.getPath()+".pdf");
        int answer=JOptionPane.showConfirmDialog(this,"Firmar este PDF con:\n"+identity.description()+"\n\nSe guardará una copia con la apariencia seleccionada.\nLa fecha procede del dispositivo; no es un sello de tiempo.","Autorizar firma",JOptionPane.OK_CANCEL_OPTION);
        if(answer!=JOptionPane.OK_OPTION)return;
        File output=target;
        work("Firmando y comprobando integridad…", () -> { PdfEngine.sign(snapshot,output,placement,identity);return output; }, file -> {
            status.setText("PDF firmado: "+file.getAbsolutePath());
            JOptionPane.showMessageDialog(this,"Guardado en:\n"+file.getAbsolutePath()+"\n\n"+PdfEngine.VERIFICATION_NOTICE,"PDF firmado",JOptionPane.INFORMATION_MESSAGE);
        });
    }
    private void verify() { if(snapshot==null)return; work("Comprobando integridad…",()->PdfEngine.verify(snapshot),v->JOptionPane.showMessageDialog(this,v.signatures==0?"El PDF no contiene firmas digitales.":v.toString())); }

    private final class Preview extends JPanel {
        BufferedImage image; float[] box; Point start; float[] before; int mode;
        Preview() {
            setBackground(new Color(226,232,240));
            MouseAdapter mouse=new MouseAdapter() {
                public void mousePressed(MouseEvent e) {
                    if(!isEnabled() || image==null)return;
                    float x=nx(e.getX()), y=ny(e.getY()); if(x<0||x>1||y<0||y>1)return;
                    start=e.getPoint(); before=box==null?null:box.clone(); mode=0;
                    if(box!=null && Math.abs(x-box[0]-box[2])*image.getWidth()<16 && Math.abs(y-box[1]-box[3])*image.getHeight()<16)mode=2;
                    else if(box!=null&&x>=box[0]&&x<=box[0]+box[2]&&y>=box[1]&&y<=box[1]+box[3])mode=1;
                }
                public void mouseDragged(MouseEvent e) {
                    if(start==null||!isEnabled())return;
                    float x=clamp(nx(e.getX())),y=clamp(ny(e.getY())),sx=nx(start.x),sy=ny(start.y);
                    if(mode==1)box=new float[]{Math.max(0,Math.min(1-before[2],before[0]+x-sx)),Math.max(0,Math.min(1-before[3],before[1]+y-sy)),before[2],before[3]};
                    else if(mode==2)box=new float[]{before[0],before[1],Math.max(.01f,x-before[0]),Math.max(.01f,y-before[1])};
                    else box=new float[]{Math.min(sx,x),Math.min(sy,y),Math.max(.001f,Math.abs(x-sx)),Math.max(.001f,Math.abs(y-sy))};
                    repaint();
                }
                public void mouseReleased(MouseEvent e) { start=null; }
            }; addMouseListener(mouse); addMouseMotionListener(mouse);
        }
        float nx(int x) { return (float)(x-20)/image.getWidth(); } float ny(int y) { return (float)(y-20)/image.getHeight(); }
        float clamp(float value) { return Math.max(0,Math.min(1,value)); }
        protected void paintComponent(Graphics graphics) {
            super.paintComponent(graphics); Graphics2D g=(Graphics2D)graphics.create();
            if(image!=null)g.drawImage(image,20,20,null);
            if(image!=null&&box!=null) {
                int x=20+Math.round(box[0]*image.getWidth()),y=20+Math.round(box[1]*image.getHeight()),w=Math.round(box[2]*image.getWidth()),h=Math.round(box[3]*image.getHeight());
                g.setColor(new Color(225,235,255,230));g.fillRect(x,y,w,h);g.setColor(new Color(35,83,155));g.setStroke(new BasicStroke(2));g.drawRect(x,y,w,h);
                g.setClip(x+4,y+2,Math.max(1,w-8),Math.max(1,h-4));g.setFont(new Font("Segoe UI",Font.PLAIN,Math.max(9,Math.min(14,h/5))));
                g.drawString("Firmado digitalmente",x+7,y+17);
                Identity identity=(Identity)identities.getSelectedItem();g.drawString(identity==null?"Selecciona tu certificado":identity.name(),x+7,y+34);
                g.drawString("Fecha y hora del dispositivo",x+7,y+51);g.setClip(null);g.fillRect(x+w-6,y+h-6,12,12);
            } g.dispose();
        }
    }
}
