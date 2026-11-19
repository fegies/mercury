mod nop_serializer;

use serde::{
    Serialize,
    ser::{
        SerializeMap, SerializeStruct, SerializeStructVariant, SerializeTuple,
        SerializeTupleStruct, SerializeTupleVariant,
    },
};
use std::convert::Infallible;
use xxhash_rust::xxh3::Xxh3Default;

use crate::scalar_extraction::nop_serializer::NopSerializer;

pub fn extract_lock_keys<'a>(
    events: impl IntoIterator<Item = &'a dyn erased_serde::Serialize>,
) -> Vec<i64> {
    let mut keys = Vec::new();
    let mut hash = Xxh3Default::new();
    for event in events {
        event
            .serialize(LockKeySerializer {
                keys: &mut keys,
                hash: &mut hash,
            })
            .expect("key extraction cannot fail");
    }
    keys.sort_unstable();
    keys.dedup();

    keys
}

struct LockKeySerializer<'buf> {
    hash: &'buf mut Xxh3Default,
    keys: &'buf mut Vec<i64>,
}

#[derive(Debug)]
struct ExtractorErr(Infallible);

impl core::fmt::Display for ExtractorErr {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.write_str("extraction error")
    }
}
impl core::error::Error for ExtractorErr {}
impl serde::ser::Error for ExtractorErr {
    fn custom<T>(_msg: T) -> Self
    where
        T: core::fmt::Display,
    {
        unimplemented!()
    }
}

impl<'a> LockKeySerializer<'a> {
    fn descender(self) -> Result<DimDescender<'a>, ExtractorErr> {
        Ok(DimDescender {
            output: self.keys,
            idx: 0,
            hash: self.hash,
        })
    }
}

#[allow(unused)]
impl<'buf> serde::Serializer for LockKeySerializer<'buf> {
    type Ok = ();
    type Error = ExtractorErr;

    type SerializeSeq = DimDescender<'buf>;
    type SerializeTuple = DimDescender<'buf>;
    type SerializeTupleStruct = DimDescender<'buf>;
    type SerializeTupleVariant = DimDescender<'buf>;
    type SerializeMap = DimDescender<'buf>;
    type SerializeStruct = DimDescender<'buf>;
    type SerializeStructVariant = DimDescender<'buf>;

    fn serialize_bool(self, v: bool) -> Result<Self::Ok, Self::Error> {
        Ok(())
    }

    fn serialize_i8(self, v: i8) -> Result<Self::Ok, Self::Error> {
        Ok(())
    }

    fn serialize_i16(self, v: i16) -> Result<Self::Ok, Self::Error> {
        Ok(())
    }

    fn serialize_i32(self, v: i32) -> Result<Self::Ok, Self::Error> {
        Ok(())
    }

    fn serialize_i64(self, v: i64) -> Result<Self::Ok, Self::Error> {
        Ok(())
    }

    fn serialize_u8(self, v: u8) -> Result<Self::Ok, Self::Error> {
        Ok(())
    }

    fn serialize_u16(self, v: u16) -> Result<Self::Ok, Self::Error> {
        Ok(())
    }

    fn serialize_u32(self, v: u32) -> Result<Self::Ok, Self::Error> {
        Ok(())
    }

    fn serialize_u64(self, v: u64) -> Result<Self::Ok, Self::Error> {
        Ok(())
    }

    fn serialize_f32(self, v: f32) -> Result<Self::Ok, Self::Error> {
        Ok(())
    }

    fn serialize_f64(self, v: f64) -> Result<Self::Ok, Self::Error> {
        Ok(())
    }

    fn serialize_char(self, v: char) -> Result<Self::Ok, Self::Error> {
        Ok(())
    }

    fn serialize_str(self, v: &str) -> Result<Self::Ok, Self::Error> {
        Ok(())
    }

    fn serialize_bytes(self, v: &[u8]) -> Result<Self::Ok, Self::Error> {
        Ok(())
    }

    fn serialize_none(self) -> Result<Self::Ok, Self::Error> {
        Ok(())
    }

    fn serialize_some<T>(self, value: &T) -> Result<Self::Ok, Self::Error>
    where
        T: ?Sized + serde::Serialize,
    {
        value.serialize(self)
    }

    fn serialize_unit(self) -> Result<Self::Ok, Self::Error> {
        Ok(())
    }

    fn serialize_unit_struct(self, name: &'static str) -> Result<Self::Ok, Self::Error> {
        Ok(())
    }

    fn serialize_unit_variant(
        self,
        name: &'static str,
        variant_index: u32,
        variant: &'static str,
    ) -> Result<Self::Ok, Self::Error> {
        Ok(())
    }

    fn serialize_newtype_struct<T>(
        self,
        name: &'static str,
        value: &T,
    ) -> Result<Self::Ok, Self::Error>
    where
        T: ?Sized + serde::Serialize,
    {
        value.serialize(self)
    }

    fn serialize_newtype_variant<T>(
        self,
        name: &'static str,
        variant_index: u32,
        variant: &'static str,
        value: &T,
    ) -> Result<Self::Ok, Self::Error>
    where
        T: ?Sized + serde::Serialize,
    {
        value.serialize(self)
    }

    fn serialize_seq(self, len: Option<usize>) -> Result<Self::SerializeSeq, Self::Error> {
        self.descender()
    }

    fn serialize_tuple(self, len: usize) -> Result<Self::SerializeTuple, Self::Error> {
        self.descender()
    }

    fn serialize_tuple_struct(
        self,
        name: &'static str,
        len: usize,
    ) -> Result<Self::SerializeTupleStruct, Self::Error> {
        self.descender()
    }

    fn serialize_tuple_variant(
        self,
        name: &'static str,
        variant_index: u32,
        variant: &'static str,
        len: usize,
    ) -> Result<Self::SerializeTupleVariant, Self::Error> {
        self.descender()
    }

    fn serialize_map(self, len: Option<usize>) -> Result<Self::SerializeMap, Self::Error> {
        self.descender()
    }

    fn serialize_struct(
        self,
        name: &'static str,
        len: usize,
    ) -> Result<Self::SerializeStruct, Self::Error> {
        self.descender()
    }

    fn serialize_struct_variant(
        self,
        name: &'static str,
        variant_index: u32,
        variant: &'static str,
        len: usize,
    ) -> Result<Self::SerializeStructVariant, Self::Error> {
        self.descender()
    }
}

struct DimDescender<'a> {
    output: &'a mut Vec<i64>,
    hash: &'a mut Xxh3Default,
    idx: u32,
}
impl<'a> DimDescender<'a> {
    fn scalar(&mut self) -> ScalarHasher<'_> {
        ScalarHasher { hash: self.hash }
    }

    fn hash_entry<T>(&mut self, key: &[u8], value: &T) -> Result<(), ExtractorErr>
    where
        T: ?Sized + Serialize,
    {
        self.hash.reset();
        self.hash.update(key);
        self.hash.update(&[0x1f]); // ascii field separator.
        value.serialize(ScalarHasher {
            hash: &mut self.hash,
        })?;
        let digest = self.hash.digest() as i64;
        self.output.push(digest);
        Ok(())
    }
}

impl serde::ser::SerializeSeq for DimDescender<'_> {
    type Ok = ();

    type Error = ExtractorErr;

    fn serialize_element<T>(&mut self, value: &T) -> Result<(), Self::Error>
    where
        T: ?Sized + Serialize,
    {
        self.idx += 1;
        self.hash_entry(&self.idx.to_le_bytes(), value)
    }

    fn end(self) -> Result<Self::Ok, Self::Error> {
        Ok(())
    }
}

impl SerializeTupleStruct for DimDescender<'_> {
    type Ok = ();

    type Error = ExtractorErr;

    fn serialize_field<T>(&mut self, value: &T) -> Result<(), Self::Error>
    where
        T: ?Sized + Serialize,
    {
        self.idx += 1;
        self.hash_entry(&self.idx.to_le_bytes(), value)
    }

    fn end(self) -> Result<Self::Ok, Self::Error> {
        Ok(())
    }
}

impl SerializeTuple for DimDescender<'_> {
    type Ok = ();

    type Error = ExtractorErr;

    fn serialize_element<T>(&mut self, value: &T) -> Result<(), Self::Error>
    where
        T: ?Sized + Serialize,
    {
        self.idx += 1;
        self.hash_entry(&self.idx.to_le_bytes(), value)
    }

    fn end(self) -> Result<Self::Ok, Self::Error> {
        Ok(())
    }
}

impl SerializeTupleVariant for DimDescender<'_> {
    type Ok = ();

    type Error = ExtractorErr;

    fn serialize_field<T>(&mut self, value: &T) -> Result<(), Self::Error>
    where
        T: ?Sized + Serialize,
    {
        self.idx += 1;
        self.hash_entry(&self.idx.to_le_bytes(), value)
    }

    fn end(self) -> Result<Self::Ok, Self::Error> {
        Ok(())
    }
}

impl SerializeMap for DimDescender<'_> {
    type Ok = ();

    type Error = ExtractorErr;

    fn serialize_key<T>(&mut self, key: &T) -> Result<(), Self::Error>
    where
        T: ?Sized + Serialize,
    {
        self.hash.reset();
        key.serialize(self.scalar())?;
        self.hash.update(&[0x1f]); // ascii field separator.
        Ok(())
    }

    fn serialize_value<T>(&mut self, value: &T) -> Result<(), Self::Error>
    where
        T: ?Sized + Serialize,
    {
        value.serialize(self.scalar())
    }

    fn end(self) -> Result<Self::Ok, Self::Error> {
        let digest = self.hash.digest() as i64;
        self.output.push(digest);
        Ok(())
    }
}

impl SerializeStruct for DimDescender<'_> {
    type Ok = ();

    type Error = ExtractorErr;

    fn serialize_field<T>(&mut self, key: &'static str, value: &T) -> Result<(), Self::Error>
    where
        T: ?Sized + Serialize,
    {
        self.hash_entry(key.as_bytes(), value)
    }

    fn end(self) -> Result<Self::Ok, Self::Error> {
        Ok(())
    }
}

impl SerializeStructVariant for DimDescender<'_> {
    type Ok = ();

    type Error = ExtractorErr;

    fn serialize_field<T>(&mut self, key: &'static str, value: &T) -> Result<(), Self::Error>
    where
        T: ?Sized + Serialize,
    {
        self.hash_entry(key.as_bytes(), value)
    }

    fn end(self) -> Result<Self::Ok, Self::Error> {
        Ok(())
    }
}

struct ScalarHasher<'a> {
    hash: &'a mut Xxh3Default,
}
impl ScalarHasher<'_> {
    fn update(self, data: &[u8]) -> Result<(), ExtractorErr> {
        self.hash.update(data);
        Ok(())
    }
}

#[allow(unused)]
impl<'a> serde::ser::Serializer for ScalarHasher<'a> {
    type Ok = ();

    type Error = ExtractorErr;

    type SerializeSeq = NopSerializer;

    type SerializeTuple = NopSerializer;

    type SerializeTupleStruct = NopSerializer;

    type SerializeTupleVariant = NopSerializer;

    type SerializeMap = NopSerializer;

    type SerializeStruct = NopSerializer;

    type SerializeStructVariant = NopSerializer;

    fn serialize_bool(self, v: bool) -> Result<Self::Ok, Self::Error> {
        self.update(&[if v { 1 } else { 0 }])
    }

    fn serialize_i8(self, v: i8) -> Result<Self::Ok, Self::Error> {
        self.update(&v.to_le_bytes())
    }

    fn serialize_i16(self, v: i16) -> Result<Self::Ok, Self::Error> {
        self.update(&v.to_le_bytes())
    }

    fn serialize_i32(self, v: i32) -> Result<Self::Ok, Self::Error> {
        self.update(&v.to_le_bytes())
    }

    fn serialize_i64(self, v: i64) -> Result<Self::Ok, Self::Error> {
        self.update(&v.to_le_bytes())
    }

    fn serialize_u8(self, v: u8) -> Result<Self::Ok, Self::Error> {
        self.update(&v.to_le_bytes())
    }

    fn serialize_u16(self, v: u16) -> Result<Self::Ok, Self::Error> {
        self.update(&v.to_le_bytes())
    }

    fn serialize_u32(self, v: u32) -> Result<Self::Ok, Self::Error> {
        self.update(&v.to_le_bytes())
    }

    fn serialize_u64(self, v: u64) -> Result<Self::Ok, Self::Error> {
        self.update(&v.to_le_bytes())
    }

    fn serialize_f32(self, v: f32) -> Result<Self::Ok, Self::Error> {
        self.update(&v.to_le_bytes())
    }

    fn serialize_f64(self, v: f64) -> Result<Self::Ok, Self::Error> {
        self.update(&v.to_le_bytes())
    }

    fn serialize_char(self, v: char) -> Result<Self::Ok, Self::Error> {
        let mut buf = [0; char::MAX_LEN_UTF8];
        self.serialize_str(v.encode_utf8(&mut buf))
    }

    fn serialize_str(self, v: &str) -> Result<Self::Ok, Self::Error> {
        self.update(v.as_bytes())
    }

    fn serialize_bytes(self, v: &[u8]) -> Result<Self::Ok, Self::Error> {
        self.update(v)
    }

    fn serialize_none(self) -> Result<Self::Ok, Self::Error> {
        Ok(())
    }

    fn serialize_some<T>(self, value: &T) -> Result<Self::Ok, Self::Error>
    where
        T: ?Sized + Serialize,
    {
        value.serialize(self)
    }

    fn serialize_unit(self) -> Result<Self::Ok, Self::Error> {
        Ok(())
    }

    fn serialize_unit_struct(self, name: &'static str) -> Result<Self::Ok, Self::Error> {
        Ok(())
    }

    fn serialize_unit_variant(
        self,
        name: &'static str,
        variant_index: u32,
        variant: &'static str,
    ) -> Result<Self::Ok, Self::Error> {
        Ok(())
    }

    fn serialize_newtype_struct<T>(
        self,
        name: &'static str,
        value: &T,
    ) -> Result<Self::Ok, Self::Error>
    where
        T: ?Sized + Serialize,
    {
        Ok(())
    }

    fn serialize_newtype_variant<T>(
        self,
        name: &'static str,
        variant_index: u32,
        variant: &'static str,
        value: &T,
    ) -> Result<Self::Ok, Self::Error>
    where
        T: ?Sized + Serialize,
    {
        Ok(())
    }

    fn serialize_seq(self, len: Option<usize>) -> Result<Self::SerializeSeq, Self::Error> {
        Ok(NopSerializer {})
    }

    fn serialize_tuple(self, len: usize) -> Result<Self::SerializeTuple, Self::Error> {
        Ok(NopSerializer {})
    }

    fn serialize_tuple_struct(
        self,
        name: &'static str,
        len: usize,
    ) -> Result<Self::SerializeTupleStruct, Self::Error> {
        Ok(NopSerializer {})
    }

    fn serialize_tuple_variant(
        self,
        name: &'static str,
        variant_index: u32,
        variant: &'static str,
        len: usize,
    ) -> Result<Self::SerializeTupleVariant, Self::Error> {
        Ok(NopSerializer {})
    }

    fn serialize_map(self, len: Option<usize>) -> Result<Self::SerializeMap, Self::Error> {
        Ok(NopSerializer {})
    }

    fn serialize_struct(
        self,
        name: &'static str,
        len: usize,
    ) -> Result<Self::SerializeStruct, Self::Error> {
        Ok(NopSerializer {})
    }

    fn serialize_struct_variant(
        self,
        name: &'static str,
        variant_index: u32,
        variant: &'static str,
        len: usize,
    ) -> Result<Self::SerializeStructVariant, Self::Error> {
        Ok(NopSerializer {})
    }
}
