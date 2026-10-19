package es.firmavisible.pdf;

import es.firmavisible.core.*;
import es.firmavisible.core.Identity;
import java.io.*;
import java.math.BigInteger;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.security.*;
import java.security.cert.X509Certificate;
import java.time.Instant;
import java.util.*;
import java.awt.image.BufferedImage;
import org.apache.pdfbox.cos.*;
import org.apache.pdfbox.pdmodel.*;
import org.apache.pdfbox.pdmodel.common.PDRectangle;
import org.apache.pdfbox.pdmodel.interactive.digitalsignature.PDSignature;
import org.apache.pdfbox.rendering.PDFRenderer;
import org.bouncycastle.asn1.cms.CMSAttributes;
import org.bouncycastle.asn1.pkcs.PKCSObjectIdentifiers;
import org.bouncycastle.asn1.x500.X500Name;
import org.bouncycastle.cert.jcajce.*;
import org.bouncycastle.cms.*;
import org.bouncycastle.operator.jcajce.JcaContentSignerBuilder;
import org.junit.jupiter.api.*;
import org.junit.jupiter.api.io.TempDir;
import static org.junit.jupiter.api.Assertions.*;

class PdfEngineTest {
    @TempDir Path directory;
    static Identity rsa,ec;
    @BeforeAll static void identities() throws Exception { rsa=identity("RSA",false);ec=identity("EC",false); }
    static Identity identity(String algorithm,boolean expired) throws Exception {
        KeyPairGenerator generator=KeyPairGenerator.getInstance(algorithm);
        if(algorithm.equals("RSA"))generator.initialize(2048);else generator.initialize(256);
        KeyPair pair=generator.generateKeyPair();X500Name name=new X500Name("CN=Persona de prueba FNMT - NO REAL,O=FirmaVisible Test");
        Instant now=Instant.now();
        var builder=new JcaX509v3CertificateBuilder(name,new BigInteger(128,new SecureRandom()),Date.from(now.minusSeconds(86400)),Date.from(now.plusSeconds(expired?-3600:86400)),name,pair.getPublic());
        X509Certificate cert=new JcaX509CertificateConverter().getCertificate(builder.build(new JcaContentSignerBuilder(algorithm.equals("RSA")?"SHA256withRSA":"SHA256withECDSA").build(pair.getPrivate())));
        return new Identity(pair.getPrivate(),new X509Certificate[]{cert});
    }
    File pdf(int rotation) throws Exception {
        File file=directory.resolve("original-"+rotation+".pdf").toFile();
        try(PDDocument doc=new PDDocument()){
            PDPage page=new PDPage(new PDRectangle(650,850));page.setCropBox(new PDRectangle(20,30,600,780));page.setRotation(rotation);doc.addPage(page);doc.addPage(new PDPage(PDRectangle.A4));
            try(PDPageContentStream cs=new PDPageContentStream(doc,page)){cs.beginText();cs.setFont(org.apache.pdfbox.pdmodel.font.PDType1Font.HELVETICA,20);cs.newLineAtOffset(50,750);cs.showText("Documento de prueba");cs.endText();}
            doc.save(file);
        }return file;
    }
    @Test void signsVisibleOnEveryRotationAndCropBox() throws Exception {
        for(int rotation:new int[]{0,90,180,270}){
            File original=pdf(rotation),output=directory.resolve("signed-"+rotation+".pdf").toFile();
            PdfEngine.sign(original,output,new Placement(0,.15f,.55f,.5f,.15f),rsa);
            var result=PdfEngine.verify(output);assertEquals(1,result.signatures);assertTrue(result.latestCoversWholeFile);
            try(PDDocument doc=PDDocument.load(output)){
                assertEquals("ETSI.CAdES.detached",doc.getSignatureDictionaries().get(0).getSubFilter());
                BufferedImage image=new PDFRenderer(doc).renderImage(0,1);
                // Blue appearance background lies exactly inside the selected displayed rectangle.
                int color=image.getRGB(Math.round(image.getWidth()*.17f),Math.round(image.getHeight()*.68f));
                java.awt.Color c=new java.awt.Color(color);assertTrue(c.getBlue()>c.getRed()&&c.getRed()>200,"Appearance misplaced at rotation "+rotation);
                int white=image.getRGB(Math.round(image.getWidth()*.1f),Math.round(image.getHeight()*.5f));assertEquals(0xffffff,white&0xffffff);
                var sig=doc.getSignatureDictionaries().get(0);
                byte[] bytes=Files.readAllBytes(output.toPath());
                byte[] cmsBytes;
                try(var asn=new org.bouncycastle.asn1.ASN1InputStream(sig.getContents(bytes))){cmsBytes=asn.readObject().getEncoded();}
                var cms=new CMSSignedData(new CMSProcessableByteArray(sig.getSignedContent(bytes)),cmsBytes);
                var attrs=cms.getSignerInfos().getSigners().iterator().next().getSignedAttributes();
                assertNotNull(attrs.get(PKCSObjectIdentifiers.id_aa_signingCertificateV2));assertNull(attrs.get(CMSAttributes.signingTime));
                Path artifacts=Paths.get("build/test-artifacts");Files.createDirectories(artifacts);
                Files.copy(output.toPath(),artifacts.resolve("signed-"+rotation+".pdf"),StandardCopyOption.REPLACE_EXISTING);
                javax.imageio.ImageIO.write(image,"png",artifacts.resolve("signed-"+rotation+".png").toFile());
            }
        }
    }
    @Test void supportsEcAndASecondSignatureIncrementally() throws Exception {
        File first=directory.resolve("first.pdf").toFile(),second=directory.resolve("second.pdf").toFile();
        PdfEngine.sign(pdf(0),first,new Placement(0,.1f,.6f,.5f,.1f),ec);
        PdfEngine.sign(first,second,new Placement(1,.2f,.5f,.5f,.12f),rsa);
        byte[] firstBytes=Files.readAllBytes(first.toPath()), secondBytes=Files.readAllBytes(second.toPath());
        assertArrayEquals(firstBytes,Arrays.copyOf(secondBytes,firstBytes.length));
        assertEquals(2,PdfEngine.verify(second).signatures);assertTrue(PdfEngine.verify(second).latestCoversWholeFile);
    }
    @Test void detectsTampering() throws Exception {
        File output=directory.resolve("tampered.pdf").toFile();PdfEngine.sign(pdf(0),output,new Placement(0,.1f,.6f,.5f,.1f),rsa);
        byte[] data=Files.readAllBytes(output.toPath());
        byte[] needle="Persona de prueba".getBytes(StandardCharsets.ISO_8859_1);
        int index=indexOf(data,needle);assertTrue(index>0);data[index]='X';Files.write(output.toPath(),data);
        assertThrows(Exception.class,()->PdfEngine.verify(output));
    }
    private int indexOf(byte[] haystack,byte[] needle){outer:for(int i=0;i<=haystack.length-needle.length;i++){for(int j=0;j<needle.length;j++)if(haystack[i+j]!=needle[j])continue outer;return i;}return -1;}
    @Test void refusesOverwriteAndPreservesOriginal() throws Exception {
        File original=pdf(0);byte[] bytes=Files.readAllBytes(original.toPath());
        assertThrows(IOException.class,()->PdfEngine.sign(original,original,new Placement(0,.1f,.6f,.5f,.1f),rsa));assertArrayEquals(bytes,Files.readAllBytes(original.toPath()));
        File existing=directory.resolve("existing.pdf").toFile();Files.writeString(existing.toPath(),"keep");
        assertThrows(IOException.class,()->PdfEngine.sign(original,existing,new Placement(0,.1f,.6f,.5f,.1f),rsa));assertEquals("keep",Files.readString(existing.toPath()));
    }
    @Test void rejectsExpiredCertificateAndTinyBox() throws Exception {
        File output=directory.resolve("invalid.pdf").toFile(),original=pdf(0);
        assertThrows(Exception.class,()->PdfEngine.sign(original,output,new Placement(0,.1f,.6f,.5f,.1f),identity("RSA",true)));assertFalse(output.exists());
        assertThrows(IOException.class,()->PdfEngine.sign(original,output,new Placement(0,.1f,.6f,.01f,.01f),rsa));assertFalse(output.exists());
    }
    @Test void certificateImportNeedsPasswordAndPrivateKey() throws Exception {
        KeyStore store=KeyStore.getInstance("PKCS12");store.load(null,null);char[] password="test-only".toCharArray();store.setKeyEntry("test",rsa.key(),password,rsa.chain());
        ByteArrayOutputStream out=new ByteArrayOutputStream();store.store(out,password);
        var loaded=Identity.fromPkcs12(new ByteArrayInputStream(out.toByteArray()),password);assertEquals(rsa.name(),loaded.get(0).name());
        assertThrows(Exception.class,()->Identity.fromPkcs12(new ByteArrayInputStream(out.toByteArray()),"wrong".toCharArray()));
    }
    @Test void reportsUnsignedAndChangesAfterSignature() throws Exception {
        File original=pdf(0),out=directory.resolve("signed.pdf").toFile();assertEquals(0,PdfEngine.verify(original).signatures);
        PdfEngine.sign(original,out,new Placement(0,.1f,.6f,.5f,.1f),rsa);Files.writeString(out.toPath(),"\n% appended\n",StandardOpenOption.APPEND);
        assertFalse(PdfEngine.verify(out).latestCoversWholeFile);
        assertThrows(IOException.class,()->PdfEngine.sign(out,directory.resolve("new.pdf").toFile(),new Placement(0,.1f,.6f,.5f,.1f),rsa));
    }
    @Test void rejectsCertifiedDocuments() throws Exception {
        File original=pdf(0),certified=directory.resolve("certified.pdf").toFile();
        try(PDDocument doc=PDDocument.load(original)){COSDictionary perms=new COSDictionary();perms.setItem(COSName.DOCMDP,new PDSignature());doc.getDocumentCatalog().getCOSObject().setItem(COSName.PERMS,perms);doc.save(certified);}
        assertThrows(IOException.class,()->PdfEngine.sign(certified,directory.resolve("refused.pdf").toFile(),new Placement(0,.1f,.6f,.5f,.1f),rsa));
    }
    @Test void refusesUnsupportedXfaWithoutProducingAnOutput() throws Exception {
        File original=pdf(0),xfa=directory.resolve("xfa.pdf").toFile(),output=directory.resolve("no-output.pdf").toFile();
        try(PDDocument doc=PDDocument.load(original)) {
            var form=new org.apache.pdfbox.pdmodel.interactive.form.PDAcroForm(doc);doc.getDocumentCatalog().setAcroForm(form);
            form.getCOSObject().setString(COSName.XFA,"<xfa/>");doc.save(xfa);
        }
        byte[] before=Files.readAllBytes(xfa.toPath());
        assertThrows(IOException.class,()->PdfEngine.sign(xfa,output,new Placement(0,.1f,.6f,.5f,.1f),rsa));
        assertFalse(output.exists());assertArrayEquals(before,Files.readAllBytes(xfa.toPath()));
    }
    @Test void rejectsOutsideAndNonFinitePlacement(){assertThrows(IllegalArgumentException.class,()->new Placement(0,.9f,.5f,.2f,.1f));assertThrows(IllegalArgumentException.class,()->new Placement(0,Float.NaN,.5f,.2f,.1f));}
}
